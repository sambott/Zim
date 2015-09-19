using System;

namespace Zim.ZimReader
{
	public enum UrlNamespace : byte
	{
		None				= 0,
		Layout 				= 0x2D,
		Articles 			= 0x41,
		ArticleMetaData		= 0x42,
		ImageFiles			= 0x49,
		ImageText			= 0x4A,
		ZimMetadata			= 0x4D,
		CategoryText		= 0x55,
		CategoryArticleList	= 0x56,
		CategoryList		= 0x57,
		FullTextIndex		= 0x58,
	}
}

