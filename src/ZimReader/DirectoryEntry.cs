using System;
using System.Collections.Generic;

namespace Zim.ZimReader
{
	public class DirectoryEntry
	{
		public enum DirectoryEntryType : short
		{
			Article = 0,
			Redirect = unchecked((short)0xffff),
			DeletedArticle = unchecked((short)0xfffe),
			LinkTarget = unchecked((short)0xfffd),
		}
		//TODO sort out the logic for other types of entry.
		public DirectoryEntryType EntryType {
			get {
				if (MimeType > unchecked((short)0xfffc)) {
					return (DirectoryEntryType)MimeType;
				} else
					return DirectoryEntryType.Article;
			}
		}

		public int RedirectIndex
		{
			get {
				if (EntryType == DirectoryEntryType.Redirect) {
					return Cluster;
				} else
					throw new FieldAccessException ("Not a Valid Field for this Entry Type");
			}
		}

		public short MimeType;
		public byte ParameterLength;
		public UrlNamespace Namespace;
		public int Revision;
		public int Cluster;
		public int Blob;
		public string Url;
		private string title;
		public List<byte> Parameters;

		public string Title {
			set {
				if (value == Url)
					title = string.Empty;
				else
					title = value;
			}
			get {
				if (title.Length == 0)
					return Url;
				return title;
			}
		}

		public DirectoryEntry ()
		{
			Parameters = new List<byte>();
		}
	}
}

