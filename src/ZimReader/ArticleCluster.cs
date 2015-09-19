using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;

namespace Zim.ZimReader
{
	public class ArticleCluster
	{
		public enum CompressionType : byte
		{
			NoCompression = 0,
			None = 1,
			//zLib = 2,
			//bZip2 = 3,
			LZMA2 = 4,
		}
		public CompressionType Compression;

		public int BlobCount;
		public int[] BlobOffsets;
		public byte[][] Blobs;

		public ArticleCluster () {
		}

		internal void PopulateArticle(Stream inputStream)
		{
			//long _streamOffset = inputStream.Position;
			int FirstBlob = inputStream.ReadLittleEndianInt32();
	
			BlobCount = (FirstBlob / 4) - 1;
			BlobOffsets = new int[BlobCount + 1];
			BlobOffsets [0] = FirstBlob;

			for (int i = 1; i <= BlobCount; i++) {
				BlobOffsets[i] = inputStream.ReadLittleEndianInt32();
			}

			Blobs = new byte[BlobCount][];
			for (int i = 0; i < BlobCount; i++) {
				int BlobLength = BlobOffsets [i + 1] - BlobOffsets [i];
				Blobs [i] = new byte [BlobLength];
				for (int j = 0; j < BlobLength; j++) {
					Blobs [i] [j] = (byte) inputStream.ReadByte ();
				}
			}

		}
	}
}

