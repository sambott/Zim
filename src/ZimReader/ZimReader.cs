using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Linq;

namespace Zim.ZimReader
{
	public class ZimReader
	{
		#region properties

		public decimal FileVersion {
			get {
				int DecimalPlaces = (int)Math.Log10 (FileMinorVersion);
				decimal tmp = FileMinorVersion;
				for (int i = 0; i <= DecimalPlaces; i++) {
					tmp /= 10;
				}
				return ((decimal)FileMajorVersion) + tmp;
			}
		}

		private long HeaderSize {
			get {
				return MimeListPosition;
			}
		}

		public Guid FileUuid { get; private set; }

		public int ArticleCount { get; private set; }

		private int? MainPageRef { // a value of 0xffffffff indicates that there is no main page
			get {
				return thisMainPageRef == 0xffffffff ? null : (int?)thisMainPageRef;
			}
			set {
				thisMainPageRef = value == null ? 0xffffffff : (uint)value;
			}
		}

		private int? LayoutPageRef { // a value of 0xffffffff indicates that there is no layout page
			get {
				return thisLayoutPageRef == 0xffffffff ? null : (int?)thisLayoutPageRef;
			}
			set {
				thisLayoutPageRef = value == null ? 0xffffffff : (uint)value;
			}
		}

		public override string ToString ()
		{
			return string.Format ("[ZimReader: FileVersion={0}, FileUuid={1}, ArticleCount={2}]", FileVersion, FileUuid, ArticleCount);
		}

		public IEnumerable<string> ArticleList
		{
			get {
				foreach (long offset in UrlPointers) {
					DirectoryEntry tmp = ReadDirectoryEntry (offset);
					if (tmp.EntryType == DirectoryEntry.DirectoryEntryType.Article)
						yield return tmp.Url;
				}
			}
		}

		#endregion

		#region fields

		private BinaryReader zimFileBinaryReader;
		
		private ushort FileMajorVersion;
		private ushort FileMinorVersion;

		private int ClusterCount;
		private long UrlPointersPosition;
		private long TitlePointersPosition;
		private long ClusterPointersPosition;
		private long MimeListPosition;
		private long ChecksumPosition;
		
		private uint thisMainPageRef;
		private uint thisLayoutPageRef;

		private List<string> MimeTypes;

		private long[] UrlPointers;
		private int[] TitlePointers;
		private long[] ClusterPointers;

		//Resolve and cache the Title and Urls at key points in the pointer array, this will speed up indexing. Essentially an index index
		private Dictionary<UrlNamespace, Dictionary<string, int>> UrlPointerPointers;
		private Dictionary<UrlNamespace, Dictionary<string, int>> TitlePointerPointers;

		#endregion

		#region events

		#endregion

		#region methods
		
		public ZimReader (Stream zimStream) //Constructor
		{
			// check stream is OK
			if (zimStream == null)
				throw new ArgumentNullException ("Null stream as argument");
			if (!zimStream.CanRead)
				throw new ArgumentException ("Cannot read from Stream");
			if (!zimStream.CanSeek)
				throw new ArgumentException ("Cannot seek in stream");

			zimFileBinaryReader = new BinaryReader (zimStream, Encoding.UTF8);

			// initialise class objects
			MimeTypes = new List<string> ();
			UrlPointerPointers = new Dictionary<UrlNamespace, Dictionary<string, int>> ();
			TitlePointerPointers = new Dictionary<UrlNamespace, Dictionary<string, int>> ();

			LoadZimFile ();

		}

		#region Loading The File
		private void LoadZimFile() {
			ReadHeader ();
			PopulateMimeTypes ();
			PopulateDirectoryPointers ();
		}

		private void ReadHeader() {
			zimFileBinaryReader.BaseStream.Seek (0, SeekOrigin.Begin);

			if (zimFileBinaryReader.ReadUInt32 () != 72173914) {   // Check that the magicNumber = 'Z','I','M',EOT
				throw new InvalidDataException ("Stream is not a valid ZIM file");
			}

			FileMajorVersion = zimFileBinaryReader.ReadUInt16 ();
			FileMinorVersion = zimFileBinaryReader.ReadUInt16 ();

			if (FileMajorVersion < 5) { // Zeno <= 3, Zim/4 = 4, ZIM >= 5
				throw new InvalidDataException ("ZIM format should be at least version 5.0");
			}

			FileUuid = new Guid (zimFileBinaryReader.ReadBytes (16));
			ArticleCount = zimFileBinaryReader.ReadInt32 ();
			ClusterCount = zimFileBinaryReader.ReadInt32 ();
			UrlPointersPosition = zimFileBinaryReader.ReadInt64 ();
			TitlePointersPosition = zimFileBinaryReader.ReadInt64 ();
			ClusterPointersPosition = zimFileBinaryReader.ReadInt64 ();
			MimeListPosition = zimFileBinaryReader.ReadInt64 ();
			MainPageRef = zimFileBinaryReader.ReadInt32 ();
			LayoutPageRef = zimFileBinaryReader.ReadInt32 ();
			ChecksumPosition = zimFileBinaryReader.ReadInt64 ();
		}

		private void PopulateMimeTypes()
		{
			zimFileBinaryReader.BaseStream.Seek (MimeListPosition, SeekOrigin.Begin);

			MimeTypes.Clear ();

			while (true) {
				string s = zimFileBinaryReader.ReadNullTerminatedString();
				if (s.Length == 0)
					break;
				MimeTypes.Add (s);
			}
		}

		private void PopulateDirectoryPointers()
		{
			ClusterPointers = new long[ClusterCount + 1];
			zimFileBinaryReader.BaseStream.Seek (ClusterPointersPosition, SeekOrigin.Begin);
			for (int Cluster = 0; Cluster < ClusterCount; Cluster++) {
				ClusterPointers [Cluster] = zimFileBinaryReader.ReadInt64 ();
			}
			ClusterPointers [ClusterCount] = ChecksumPosition; //TODO verify this is a correct assumption

			UrlPointers = new long[ArticleCount];
			zimFileBinaryReader.BaseStream.Seek (UrlPointersPosition, SeekOrigin.Begin);
			for (int Article = 0; Article < ArticleCount; Article++) {
				UrlPointers[Article] = zimFileBinaryReader.ReadInt64 ();
			}

			System.Diagnostics.Debug.Assert (zimFileBinaryReader.BaseStream.Position == TitlePointersPosition);

			TitlePointers = new int[ArticleCount];
			for (int Article = 0; Article < ArticleCount; Article++) {
				TitlePointers[Article] = zimFileBinaryReader.ReadInt32 ();
			}

			UrlPointerPointers.Clear ();
			TitlePointerPointers.Clear ();
			// Yes - I know this /could/ be put into one of the above loops but this way we minimise the seeking and can use the read buffer for the above.
			int IndexPointsToCache = ArticleCount < 40 ? ArticleCount : 40;
			UrlNamespace tmpName;
			string tmpStr;
			for (int i = 0; i < IndexPointsToCache; i++) {

				//the superfluous casting here is to make sure we aren't multiplying ArticleCount as an intermediate step above Max(Int32)
				int ind = (int)(((((long)ArticleCount) - 1) * i) / (IndexPointsToCache - 1));

				tmpStr = ReadDirectoryEntryUrl(UrlPointers[ind]);
				tmpName = ReadDirectoryEntryNamespace (UrlPointers[ind]);
				if (!UrlPointerPointers.ContainsKey (tmpName))
				{
					UrlPointerPointers.Add (tmpName, new Dictionary<string, int> ());
				}
				UrlPointerPointers[tmpName].Add (tmpStr, ind);

				tmpStr = ReadDirectoryEntryTitle (UrlPointers [TitlePointers [ind]]);
				tmpName = ReadDirectoryEntryNamespace (UrlPointers [TitlePointers [ind]]);
				if (!TitlePointerPointers.ContainsKey (tmpName))
				{
					TitlePointerPointers.Add (tmpName, new Dictionary<string, int> ());
				}
				TitlePointerPointers[tmpName].Add (tmpStr, ind);
			}
		}
		#endregion

		private DirectoryEntry ReadDirectoryEntry(long Offset)
		{
			zimFileBinaryReader.BaseStream.Seek (Offset, SeekOrigin.Begin);
			DirectoryEntry deOutput = new DirectoryEntry ();
			deOutput.MimeType = zimFileBinaryReader.ReadInt16 ();
			deOutput.ParameterLength = zimFileBinaryReader.ReadByte ();
			deOutput.Namespace = (UrlNamespace) zimFileBinaryReader.ReadByte ();
			deOutput.Revision = zimFileBinaryReader.ReadInt32 ();

			switch (deOutput.EntryType) {

			case DirectoryEntry.DirectoryEntryType.Article:
				deOutput.Cluster = zimFileBinaryReader.ReadInt32 ();
				deOutput.Blob = zimFileBinaryReader.ReadInt32 ();
				deOutput.Url = zimFileBinaryReader.ReadNullTerminatedString ();
				deOutput.Title = zimFileBinaryReader.ReadNullTerminatedString ();
				deOutput.Parameters.AddRange (zimFileBinaryReader.ReadBytes(deOutput.ParameterLength));
				break;

			case DirectoryEntry.DirectoryEntryType.Redirect:
				deOutput.Cluster = zimFileBinaryReader.ReadInt32 ();
				deOutput.Url = zimFileBinaryReader.ReadNullTerminatedString ();
				deOutput.Title = zimFileBinaryReader.ReadNullTerminatedString ();
				deOutput.Parameters.AddRange (zimFileBinaryReader.ReadBytes(deOutput.ParameterLength));
				break;

			case DirectoryEntry.DirectoryEntryType.LinkTarget:
				goto case DirectoryEntry.DirectoryEntryType.DeletedArticle;
			case DirectoryEntry.DirectoryEntryType.DeletedArticle:
				zimFileBinaryReader.BaseStream.Seek (8, SeekOrigin.Current);
				deOutput.Url = zimFileBinaryReader.ReadNullTerminatedString ();
				deOutput.Title = zimFileBinaryReader.ReadNullTerminatedString ();
				deOutput.Parameters.AddRange (zimFileBinaryReader.ReadBytes(deOutput.ParameterLength));
				break;

			}

			return deOutput;
		}
		private string ReadDirectoryEntryUrl (long Offset)
		{
			zimFileBinaryReader.BaseStream.Seek (Offset, SeekOrigin.Begin);
			long UrlOffset;
			switch (zimFileBinaryReader.ReadUInt16 ()) {
			
			case 0xffff: //redirect
				UrlOffset = 12L;
				break;
			//case 0xfffe: //linktarget
			//case 0xfffd: //deleted
			default: //article
				UrlOffset = 16L;
				break;
			}
			zimFileBinaryReader.BaseStream.Seek (Offset + UrlOffset, SeekOrigin.Begin);
			return zimFileBinaryReader.ReadNullTerminatedString ();
		}
		private string ReadDirectoryEntryTitle(long Offset)
		{
			string Url = ReadDirectoryEntryUrl (Offset);
			string Title = zimFileBinaryReader.ReadNullTerminatedString ();
			return Title.Length == 0 ? Url : Title;
		}
		private UrlNamespace ReadDirectoryEntryNamespace (long Offset)
		{
			zimFileBinaryReader.BaseStream.Seek (Offset + 3, SeekOrigin.Begin);
			return (UrlNamespace)zimFileBinaryReader.ReadByte();
		}

		private ArticleCluster GetArticleCluster (int ClusterNumber)
		{
			ArticleCluster AC = new ArticleCluster ();
			zimFileBinaryReader.BaseStream.Seek (ClusterPointers [ClusterNumber], SeekOrigin.Begin);

			AC.Compression = (ArticleCluster.CompressionType)zimFileBinaryReader.ReadByte ();

			switch (AC.Compression) {

			case ArticleCluster.CompressionType.LZMA2:
				AC.PopulateArticle (XZ.OpenXZ (zimFileBinaryReader));
				break;

			default:
				AC.PopulateArticle (zimFileBinaryReader.BaseStream);
				break;
			}

			return AC;
		}

		private Article GetArticle(DirectoryEntry DE)
		{
			Article A = new Article ();
			A.DirectoryEntry = DE;

			ArticleCluster AC = GetArticleCluster (DE.Cluster);
			A.Body = AC.Blobs[DE.Blob];
			return A;

		}

		public bool UrlExists(string Url)
		{
			//TODO
			return false;
		}
		public bool TitleExists(string Title)
		{
			//TODO
			return false;
		}

		public Article GetArticleByUrl (string Url, UrlNamespace ArticleType = UrlNamespace.Articles)
		{
			if (!UrlPointerPointers.ContainsKey (ArticleType))
				throw new ArgumentException ("File contains no Articles of type " + ArticleType.ToString ());

			Dictionary<string, int> DicToRef = UrlPointerPointers [ArticleType];
			int UrlRef;
			StringComparer SC = StringComparer.InvariantCulture;

			if (DicToRef.ContainsKey (Url)) {
				UrlRef = DicToRef [Url];
			} else {

				int LowerBound,UpperBound,TestValue,TestRes;

				var SortedKeys = DicToRef.Keys.ToList();
				SortedKeys.Sort();

				int i = 0;
				if (SC.Compare(Url, SortedKeys[i]) < 0) throw new ArgumentException("Cannot Find Article");
				do {
					LowerBound = DicToRef[SortedKeys[i++]];
				} while (SC.Compare(Url, SortedKeys[i]) > 0);
				
				i = SortedKeys.Count - 1;
				if (SC.Compare(Url, SortedKeys[i]) > 0) throw new ArgumentException("Cannot Find Article");
				do {
					UpperBound = DicToRef[SortedKeys[i--]];
				} while (SC.Compare(Url, SortedKeys[i]) < 0);

				while (true)
				{
					TestValue = ((UpperBound - LowerBound) / 2) + LowerBound;
					if (TestValue == LowerBound) throw new ArgumentException("Cannot Find Article");
					string tmpStr = ReadDirectoryEntryUrl(UrlPointers[TestValue]);
					TestRes = SC.Compare(Url, tmpStr);
					if (TestRes == 0)
					{
						//Found it - lets save its location in our quick reference kkv trips
						UrlRef = TestValue;
						UrlPointerPointers[ReadDirectoryEntryNamespace (UrlPointers[UrlRef])].Add (tmpStr, UrlRef);
						break;
					}
					else if (TestRes < 0)
					{
						UpperBound = TestValue;
					}
					else
					{
						LowerBound = TestValue;
					}
				}
			}
			return GetArticle(ReadDirectoryEntry(UrlPointers[UrlRef]));
		}

		public Article GetArticleByTitle (string Title, UrlNamespace ArticleType = UrlNamespace.Articles)
		{
			//TODO
			return new Article();
		}

		public string[] SearchByTitle(string Title, bool CaseSensitive = true, UrlNamespace ArticleType = UrlNamespace.Articles, int Results = 5)
		{
			//TODO - plus handle some kind of entry cache
			return new string[Results];
		}

		public string[] SearchByUrl(string Url, bool CaseSensitive = true, UrlNamespace ArticleType = UrlNamespace.Articles, int Results = 5)
		{
			//TODO - plus handle some kind of entry cache
			return new string[Results];
		}

		#endregion
	}
}

