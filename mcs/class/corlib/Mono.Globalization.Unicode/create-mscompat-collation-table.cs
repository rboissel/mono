//
//
// There are two kind of sort keys : which are computed and which are laid out
// as an indexed array. Computed sort keys are:
//
//	- Surrogate
//	- PrivateUse
//
// Also, for composite characters it should prepare different index table.
//
// It is possible to "compute" level 3 weights, they are still dumped to
// an array to avoid execution cost.
//

//
// * sortkey getter signature
//
//	int GetSortKey (string s, int index, SortKeyBuffer buf)
//	Stores sort key for corresponding character element into buf and
//	returns the length of the consumed _source_ character element in s.
//
// * character length to consume
//
//	If there are characters whose primary weight is 0, they are consumed
//	and considered as a part of the character element.
//

using System;
using System.IO;
using System.Collections;
using System.Globalization;
using System.Xml;

namespace Mono.Globalization.Unicode
{
	internal class MSCompatSortKeyTableGenerator
	{
		public static void Main (string [] args)
		{
			new MSCompatSortKeyTableGenerator ().Run (args);
		}

		const int DecompositionFull = 1; // fixed
		const int DecompositionSub = 2; // fixed
		const int DecompositionSmall = 3;
		const int DecompositionIsolated = 4;
		const int DecompositionInitial = 5;
		const int DecompositionFinal = 6;
		const int DecompositionMedial = 7;
		const int DecompositionNoBreak = 8;
		const int DecompositionCompat = 9;
		const int DecompositionFraction = 0xA;
		const int DecompositionFont = 0xB;
		const int DecompositionCircle = 0xC;
		const int DecompositionSquare = 0xD;
		const int DecompositionSuper = 0xE; // fixed
		const int DecompositionWide = 0xF;
		const int DecompositionNarrow = 0x10;
		const int DecompositionVertical = 0x11;

		TextWriter Result = Console.Out;

		byte [] fillIndex = new byte [256]; // by category
		CharMapEntry [] map = new CharMapEntry [char.MaxValue + 1];

		char [] specialIgnore = new char [] {
			'\u3099', '\u309A', '\u309B', '\u309C', '\u0BCD',
			'\u0E47', '\u0E4C', '\uFF9E', '\uFF9F'
			};

		// FIXME: need more love (as always)
		char [] alphabets = new char [] {'A', 'B', 'C', 'D', 'E', 'F',
			'G', 'H', 'I', 'J', 'K', 'L', 'M', 'N', 'O', 'P', 'Q',
			'R', 'S', 'T', 'U', 'V', 'W', 'X', 'Y', 'Z',
			'\u0292', '\u01BE', '\u0298'};
		byte [] alphaWeights = new byte [] {
			2, 9, 0xA, 0x1A, 0x21,
			0x23, 0x25, 0x2C, 0x32, 0x35,
			0x36, 0x48, 0x51, 0x70, 0x7C,
			0x7E, 0x89, 0x8A, 0x91, 0x99,
			0x9F, 0xA2, 0xA4, 0xA6, 0xA7,
			0xA9, 0xAA, 0xB3, 0xB4};

		bool [] isSmallCapital = new bool [char.MaxValue + 1];
		bool [] isUppercase = new bool [char.MaxValue + 1];

		byte [] decompType = new byte [char.MaxValue + 1];
		int [] decompIndex = new int [char.MaxValue + 1];
		int [] decompLength = new int [char.MaxValue + 1];
		int [] decompValues;
		decimal [] decimalValue = new decimal [char.MaxValue + 1];

		byte [] diacritical = new byte [char.MaxValue + 1];

		string [] diacritics = new string [] {
			// LATIN
			" ACUTE;", " GRAVE;", " DOT ABOVE;", " MIDDLE DOT;",
			" CIRCUMFLEX;", " DIAERESIS;", " CARON;", " BREVE;",
			" DIALYTIKA AND TONOS;", " MACRON;", " TILDE;", " RING ABOVE;",
			" OGONEK;", " CEDILLA;",
			" DOUBLE ACUTE;", " ACUTE AND DOT ABOVE;",
			" STROKE;", " CIRCUMFLEX AND ACUTE;",
			" DIAERESIS AND ACUTE;", "WITH CIRCUMFLEX AND GRAVE;", " L SLASH;",
			" DIAERESIS AND GRAVE;",
			" BREVE AND ACUTE;",
			" CARON AND DOT ABOVE;", " BREVE AND GRAVE;",
			" MACRON AND ACUTE;",
			" MACRON AND GRAVE;",
			" DIAERESIS AND CARON", " DOT ABOVE AND MACRON", " TILDE AND ACUTE",
			" RING ABOVE AND ACUTE",
			" DIAERESIS AND MACRON", " CEDILLA AND ACUTE", " MACRON AND DIAERESIS",
			" CIRCUMFLEX AND TILDE",
			" TILDE AND DIAERESIS",
			" STROKE AND ACUTE",
			" BREVE AND TILDE",
			" CEDILLA AND BREVE",
			" OGONEK AND MACRON",
			" HOOK;", "LEFT HOOK;", " WITH HOOK ABOVE;",
			" DOUBLE GRAVE;",
			" INVERTED BREVE",
			" PRECEDED BY APOSTROPHE",
			" HORN;",
			" LINE BELOW;", " CIRCUMFLEX AND HOOK ABOVE",
			" PALATAL HOOK",
			" DOT BELOW;",
			" RETROFLEX;", "DIAERESIS BELOW",
			" RING BELOW",
			" CIRCUMFLEX BELOW", "HORN AND ACUTE",
			" BREVE BELOW;", " HORN AND GRAVE",
			" TILDE BELOW",
			" DOT BELOW AND DOT ABOVE",
			" RIGHT HALF RING", " HORN AND TILDE",
			" CIRCUMFLEX AND DOT BELOW",
			" BREVE AND DOT BELOW",
			" DOT BELOW AND MACRON",
			" HORN AND HOOK ABOVE",
			" HORN AND DOT",
			// CIRCLED, PARENTHESIZED and so on
			"CIRCLED DIGIT", "CIRCLED NUMBER", "CIRCLED LATIN", "CIRCLED KATAKANA",
			"PARENTHESIZED DIGIT", "PARENTHESIZED NUMBER", "PARENTHESIZED LATIN",
			};
		byte [] diacriticWeights = new byte [] {
			// LATIN.
			0xE, 0xF, 0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16,
			0x17, 0x19, 0x1A, 0x1B, 0x1C,
			0x1D, 0x1D, 0x1E, 0x1E, 0x1F, 0x1F, 0x1F,
			0x20, 0x21, 0x22, 0x22, 0x23, 0x24,
			0x25, 0x25, 0x25, 0x26, 0x28, 0x28, 0x28,
			0x29, 0x2A, 0x2B, 0x2C, 0x2F, 0x30,
			0x43, 0x43, 0x43, 0x44, 0x46, 0x48,
			0x52, 0x55, 0x55, 0x57, 0x58, 0x59, 0x59, 0x5A,
			0x60, 0x60, 0x61, 0x61, 0x63, 0x68, 
			0x69, 0x69, 0x6A, 0x6D, 0x6E,
			0x95, 0xAA,
			// CIRCLED, PARENTHESIZED and so on.
			0xEE, 0xEE, 0xEE, 0xEE, 0xF3, 0xF3, 0xF3, 0xF3
			};

		int [] numberSecondaryWeightBounds = new int [] {
			0x660, 0x680, 0x6F0, 0x700, 0x960, 0x970,
			0x9E0, 0x9F0, 0x9F4, 0xA00, 0xA60, 0xA70,
			0xAE0, 0xAF0, 0xB60, 0xB70, 0xBE0, 0xC00,
			0xC60, 0xC70, 0xCE0, 0xCF0, 0xD60, 0xD70,
			0xE50, 0xE60, 0xED0, 0xEE0
			};

		char [] orderedCyrillic;
		char [] orderedGurmukhi;
		char [] orderedGujarati;
		char [] orderedGeorgian;
		char [] orderedThaana;

		static readonly char [] orderedTamilConsonants = new char [] {
			// based on traditional Tamil consonants, except for
			// Grantha (where Microsoft breaks traditionalism).
			// http://www.angelfire.com/empire/thamizh/padanGaL
			'\u0B99', '\u0B9A', '\u0B9E', '\u0B9F', '\u0BA3',
			'\u0BA4', '\u0BA8', '\u0BAA', '\u0BAE', '\u0BAF',
			'\u0BB0', '\u0BB2', '\u0BB5', '\u0BB4', '\u0BB3',
			'\u0BB1', '\u0BA9', '\u0B9C', '\u0BB8', '\u0BB7',
			'\u0BB9'};

		Hashtable arabicLetterPrimaryValues = new Hashtable (); // cp -> level1 value
		Hashtable arabicNameMap = new Hashtable (); // letterName -> cp

		ArrayList jisJapanese = new ArrayList ();
		ArrayList nonJisJapanese = new ArrayList ();

		ushort [] cjkJA = new ushort [char.MaxValue - 0x4E00];
		ushort [] cjkCHS = new ushort [char.MaxValue - 0x3100];
		ushort [] cjkCHT = new ushort [char.MaxValue - 0x4E00];
		ushort [] cjkKO = new ushort [char.MaxValue - 0x4E00];
		byte [] cjkKOlv2 = new byte [char.MaxValue - 0x4E00];

		void Run (string [] args)
		{
			string dirname = args.Length == 0 ? "downloaded" : args [0];
			ParseSources (dirname);
			Console.Error.WriteLine ("parse done.");
			InterpretParsedData ();
			Console.Error.WriteLine ("interpretation done.");
			Generate ();
			Console.Error.WriteLine ("generation done.");
			Serialize ();
			Console.Error.WriteLine ("serialization done.");
		}

		void Serialize ()
		{
			// Primary category
			Result.WriteLine ("int [] categories = new int [] {");
			for (int i = 0; i < map.Length; i++) {
				byte value = map [i].Category;
				if (value == 0)
					Result.Write ("0,");
				else
					Result.Write ("0x{0:X02},", value);
				if ((i & 0xF) == 0xF)
					Result.WriteLine ("// {0:X04}", i - 0xF);
			}
			Result.WriteLine ("};");
			Result.WriteLine ();

			// Primary weight value
			Result.WriteLine ("static int [] level1 = new int [] {");
			for (int i = 0; i < map.Length; i++) {
				byte value = map [i].Level1;
				if (value == 0)
					Result.Write ("0,");
				else
					Result.Write ("0x{0:X02},", value);
				if ((i & 0xF) == 0xF)
					Result.WriteLine ("// {0:X04}", i - 0xF);
			}
			Result.WriteLine ("};");
			Result.WriteLine ();

			// Secondary weight
			Result.WriteLine ("static int [] level2 = new int [] {");
			for (int i = 0; i < map.Length; i++) {
				int value = map [i].Level2;
				if (value == 0)
					Result.Write ("0,");
				else
					Result.Write ("0x{0:X02},", value);
				if ((i & 0xF) == 0xF)
					Result.WriteLine ("// {0:X04}", i - 0xF);
			}
			Result.WriteLine ("};");
			Result.WriteLine ();

			// Thirtiary weight
			Result.WriteLine ("static byte [] level3 = new byte [] {");
			for (int i = 0; i < map.Length; i++) {
				byte value = ComputeLevel3WeightRaw ((char) i);
				if (value == 0)
					Result.Write ("0,");
				else
					Result.Write ("0x{0:X02},", value);
				if ((i & 0xF) == 0xF)
					Result.WriteLine ("// {0:X04}", i - 0xF);
			}
			Result.WriteLine ("};");
			Result.WriteLine ();

			// Width insensitivity mappings
			// (for now it is more lightweight than dumping the
			// entire NFKD table).
			Result.WriteLine ("static int [] widthCompat = new int [] {");
			for (int i = 0; i < char.MaxValue; i++) {
				int value = 0;
				switch (decompType [i]) {
				case DecompositionNarrow:
				case DecompositionWide:
				case DecompositionSuper:
				case DecompositionSub:
					// they are always 1 char
					value = decompValues [decompIndex [i]];
					break;
				}
				if (value == 0)
					Result.Write ("0,");
				else
					Result.Write ("0x{0:X04},", value);
				if ((i & 0xF) == 0xF)
					Result.WriteLine ("// {0:X04}", i - 0xF);
			}
			Result.WriteLine ("};");
			Result.WriteLine ();

			// CJK
			SerializeCJK ("cjkCHS", cjkCHS, char.MaxValue);
			SerializeCJK ("cjkCHT", cjkCHT, 0x9FB0);
			SerializeCJK ("cjkJA", cjkJA, 0x9FB0);
			SerializeCJK ("cjkKO", cjkKO, 0x9FB0);
			SerializeCJK ("cjkKOlv2", cjkKOlv2, 0x9FB0);
		}

		void SerializeCJK (string name, ushort [] cjk, int max)
		{
			int offset = char.MaxValue - cjk.Length;
			Result.WriteLine ("static ushort [] {0} = new ushort [] {{", name);
			for (int i = 0; i < cjk.Length; i++) {
				if (i + offset == max)
					break;
				ushort value = cjk [i];
				if (value == 0)
					Result.Write ("0,");
				else
					Result.Write ("0x{0:X04},", value);
				if ((i & 0xF) == 0xF)
					Result.WriteLine ("// {0:X04}", i - 0xF + offset);
			}
			Result.WriteLine ("};");
			Result.WriteLine ();
		}

		void SerializeCJK (string name, byte [] cjk, int max)
		{
			int offset = char.MaxValue - cjk.Length;
			Result.WriteLine ("static byte [] {0} = new byte [] {{", name);
			for (int i = 0; i < cjk.Length; i++) {
				if (i + offset == max)
					break;
				byte value = cjk [i];
				if (value == 0)
					Result.Write ("0,");
				else
					Result.Write ("0x{0:X02},", value);
				if ((i & 0xF) == 0xF)
					Result.WriteLine ("// {0:X04}", i - 0xF + offset);
			}
			Result.WriteLine ("};");
			Result.WriteLine ();
		}

		#region Parse

		void ParseSources (string dirname)
		{
			string unidata =
				dirname + "/UnicodeData.txt";
			string derivedCoreProps = 
				dirname + "/DerivedCoreProperties.txt";
			string scripts = 
				dirname + "/Scripts.txt";
			string cp932 = 
				dirname + "/CP932.TXT";
			string chXML = dirname + "/common/collation/zh.xml";
			string jaXML = dirname + "/common/collation/ja.xml";
			string koXML = dirname + "/common/collation/ko.xml";

			ParseJISOrder (cp932); // in prior to ParseUnidata()
			ParseUnidata (unidata);
			ParseDerivedCoreProperties (derivedCoreProps);
			ParseScripts (scripts);
			ParseCJK (chXML, jaXML, koXML);
		}

		void ParseUnidata (string filename)
		{
			ArrayList decompValues = new ArrayList ();
			using (StreamReader unidata =
				new StreamReader (filename)) {
				for (int line = 1; unidata.Peek () >= 0; line++) {
					try {
						ProcessUnidataLine (unidata.ReadLine (), decompValues);
					} catch (Exception) {
						Console.Error.WriteLine ("**** At line " + line);
						throw;
					}
				}
			}
			this.decompValues = (int [])
				decompValues.ToArray (typeof (int));
		}
		
		void ProcessUnidataLine (string s, ArrayList decompValues)
		{
			int idx = s.IndexOf ('#');
			if (idx >= 0)
				s = s.Substring (0, idx);
			idx = s.IndexOf (';');
			if (idx < 0)
				return;
			int cp = int.Parse (s.Substring (0, idx), NumberStyles.HexNumber);
			string [] values = s.Substring (idx + 1).Split (';');

			// FIXME: use index
			if (cp > char.MaxValue)
				return;

			// isSmallCapital
			if (s.IndexOf ("SMALL CAPITAL") > 0)
				isSmallCapital [cp] = true;

			for (int d = 0; d < diacritics.Length; d++)
				if (s.IndexOf (diacritics [d]) > 0)
					diacritical [cp] |= diacriticWeights [d];
			// Two-step grep required for it.
			if (s.IndexOf ("FULL STOP") > 0 &&
				(s.IndexOf ("DIGIT") > 0 || s.IndexOf ("NUMBER") > 0))
				diacritical [cp] |= 0xF4;

			// Arabic letter name
			if (0x0621 <= cp && cp <= 0x064A &&
				Char.GetUnicodeCategory ((char) cp)
				== UnicodeCategory.OtherLetter) {
				byte value = (byte) (arabicNameMap.Count * 4 + 0x0B);
				switch (cp) {
				case 0x0621:
				case 0x0624:
				case 0x0626:
					// hamza, waw, yeh ... special cases.
					value = 0x07;
					break;
				case 0x0649:
				case 0x064A:
					value = 0x77; // special cases.
					break;
				default:
					// Get primary letter name i.e.
					// XXX part of ARABIC LETTER XXX yyy
					// e.g. that of "TEH MARBUTA" is "TEH".
					string letterName =
						(cp == 0x0640) ?
						// 0x0640 is special: it does
						// not start with ARABIC LETTER
						values [0] :
						values [0].Substring (14);
					int tmpIdx = letterName.IndexOf (' ');
					letterName = tmpIdx < 0 ? letterName : letterName.Substring (0, tmpIdx);
//Console.Error.WriteLine ("Arabic name for {0:X04} is {1}", cp, letterName);
					if (arabicNameMap.ContainsKey (letterName))
						value = (byte) arabicLetterPrimaryValues [arabicNameMap [letterName]];
					else
						arabicNameMap [letterName] = cp;
					break;
				}
				arabicLetterPrimaryValues [cp] = value;
			}

			// Japanese square letter
			if (0x3300 <= cp && cp <= 0x3357)
				if (!ExistsJIS (cp))
					nonJisJapanese.Add (new NonJISCharacter (cp, values [0]));

			// normalizationType
			string decomp = values [4];
			idx = decomp.IndexOf ('<');
			if (idx >= 0) {
				switch (decomp.Substring (idx + 1, decomp.IndexOf ('>') - 1)) {
				case "full":
					decompType [cp] = DecompositionFull;
					break;
				case "sub":
					decompType [cp] = DecompositionSub;
					break;
				case "super":
					decompType [cp] = DecompositionSuper;
					break;
				case "small":
					decompType [cp] = DecompositionSmall;
					break;
				case "isolated":
					decompType [cp] = DecompositionIsolated;
					break;
				case "initial":
					decompType [cp] = DecompositionInitial;
					break;
				case "final":
					decompType [cp] = DecompositionFinal;
					break;
				case "medial":
					decompType [cp] = DecompositionMedial;
					break;
				case "noBreak":
					decompType [cp] = DecompositionNoBreak;
					break;
				case "compat":
					decompType [cp] = DecompositionCompat;
					break;
				case "fraction":
					decompType [cp] = DecompositionFraction;
					break;
				case "font":
					decompType [cp] = DecompositionFont;
					break;
				case "circle":
					decompType [cp] = DecompositionCircle;
					break;
				case "square":
					decompType [cp] = DecompositionSquare;
					break;
				case "wide":
					decompType [cp] = DecompositionWide;
					break;
				case "narrow":
					decompType [cp] = DecompositionNarrow;
					break;
				case "vertical":
					decompType [cp] = DecompositionVertical;
					break;
				default:
					throw new Exception ("Support NFKD type : " + decomp);
				}
			}
			decomp = idx < 0 ? decomp : decomp.Substring (decomp.IndexOf ('>') + 2);
			if (decomp.Length > 0) {
				string [] velems = decomp.Split (' ');
				decompIndex [cp] = decompValues.Count;
				foreach (string v in velems)
					decompValues.Add (int.Parse (v, NumberStyles.HexNumber));
				decompLength [cp] = velems.Length;
			}
			// numeric values
			if (values [5].Length > 0)
				decimalValue [cp] = decimal.Parse (values [5]);
			else if (values [6].Length > 0)
				decimalValue [cp] = decimal.Parse (values [6]);
			else if (values [7].Length > 0) {
				idx = values [7].IndexOf ('/');
				if (idx > 0)
					decimalValue [cp] = 
						decimal.Parse (values [7].Substring (0, idx))
						/ decimal.Parse (values [7].Substring (idx + 1));
			}
		}

		void ParseDerivedCoreProperties (string filename)
		{
			// IsUppercase
			using (StreamReader file =
				new StreamReader (filename)) {
				for (int line = 1; file.Peek () >= 0; line++) {
					try {
						ProcessDerivedCorePropLine (file.ReadLine ());
					} catch (Exception) {
						Console.Error.WriteLine ("**** At line " + line);
						throw;
					}
				}
			}
		}

		void ProcessDerivedCorePropLine (string s)
		{
			int idx = s.IndexOf ('#');
			if (idx >= 0)
				s = s.Substring (0, idx);
			idx = s.IndexOf (';');
			if (idx < 0)
				return;
			string cpspec = s.Substring (0, idx);
			idx = cpspec.IndexOf ("..");
			NumberStyles nf = NumberStyles.HexNumber |
				NumberStyles.AllowTrailingWhite;
			int cp = int.Parse (idx < 0 ? cpspec : cpspec.Substring (0, idx), nf);
			int cpEnd = idx < 0 ? cp : int.Parse (cpspec.Substring (idx + 2), nf);
			string value = s.Substring (cpspec.Length + 1).Trim ();

			// FIXME: use index
			if (cp > char.MaxValue)
				return;

			switch (value) {
			case "Uppercase":
				for (int x = cp; x <= cpEnd; x++)
					isUppercase [x] = true;
				break;
			}
		}

		void ParseScripts (string filename)
		{
			ArrayList cyrillic = new ArrayList ();
			ArrayList gurmukhi = new ArrayList ();
			ArrayList gujarati = new ArrayList ();
			ArrayList georgian = new ArrayList ();
			ArrayList thaana = new ArrayList ();

			using (StreamReader file =
				new StreamReader (filename)) {
				while (file.Peek () >= 0) {
					string s = file.ReadLine ();
					int idx = s.IndexOf ('#');
					if (idx >= 0)
						s = s.Substring (0, idx);
					idx = s.IndexOf (';');
					if (idx < 0)
						continue;

					string cpspec = s.Substring (0, idx);
					idx = cpspec.IndexOf ("..");
					NumberStyles nf = NumberStyles.HexNumber |
						NumberStyles.AllowTrailingWhite;
					int cp = int.Parse (idx < 0 ? cpspec : cpspec.Substring (0, idx), nf);
					int cpEnd = idx < 0 ? cp : int.Parse (cpspec.Substring (idx + 2), nf);
					string value = s.Substring (cpspec.Length + 1).Trim ();

					// FIXME: use index
					if (cp > char.MaxValue)
						continue;

					switch (value) {
					case "cyrillic":
						for (int x = cp; x <= cpEnd; x++)
							cyrillic.Add ((char) x);
						break;
					case "Gurmukhi":
						for (int x = cp; x <= cpEnd; x++)
							gurmukhi.Add ((char) x);
						break;
					case "Gujarati":
						for (int x = cp; x <= cpEnd; x++)
							gujarati.Add ((char) x);
						break;
					case "Georgia":
						for (int x = cp; x <= cpEnd; x++)
							georgian.Add ((char) x);
						break;
					case "Thaana":
						for (int x = cp; x <= cpEnd; x++)
							thaana.Add ((char) x);
						break;
					}
				}
			}
			cyrillic.Sort (UCAComparer.Instance);
			gurmukhi.Sort (UCAComparer.Instance);
			gujarati.Sort (UCAComparer.Instance);
			georgian.Sort (UCAComparer.Instance);
			thaana.Sort (UCAComparer.Instance);
			orderedCyrillic = (char []) cyrillic.ToArray (typeof (char));
			orderedGurmukhi = (char []) gurmukhi.ToArray (typeof (char));
			orderedGujarati = (char []) gujarati.ToArray (typeof (char));
			orderedGeorgian = (char []) georgian.ToArray (typeof (char));
			orderedThaana = (char []) thaana.ToArray (typeof (char));
		}

		void ParseJISOrder (string filename)
		{
			using (StreamReader file =
				new StreamReader (filename)) {
				while (file.Peek () >= 0) {
					string s = file.ReadLine ();
					int idx = s.IndexOf ('#');
					if (idx >= 0)
						s = s.Substring (0, idx).Trim ();
					if (s.Length == 0)
						continue;
					idx = s.IndexOf (' ');
					if (idx < 0)
						continue;
					// They start with "0x" so cut them out.
					int jis = int.Parse (s.Substring (2, idx), NumberStyles.HexNumber);
					int cp = int.Parse (s.Substring (idx + 3).Trim (), NumberStyles.HexNumber);
					jisJapanese.Add (new JISCharacter (cp, jis));
				}
			}
		}

		void ParseCJK (string zhXML, string jaXML, string koXML)
		{
			XmlDocument doc = new XmlDocument ();
			doc.XmlResolver = null;
			int v;
			string s;
			string category;
			int offset;
			ushort [] arr;

			// Chinese Simplified
			category = "chs";
			arr = cjkCHS;
			offset = char.MaxValue - arr.Length;
			doc.Load (zhXML);
			s = doc.SelectSingleNode ("/ldml/collations/collation[@type='pinyin']/rules/pc").InnerText;
			v = 0x8008;
			foreach (char c in s) {
				if (c < '\u3100')
					Console.Error.WriteLine ("---- warning: for {0} {1:X04} is omitted which should be {2:X04}", category, (int) c, v);
				else {
					arr [(int) c - offset] = (ushort) v++;
					if (v % 256 == 0)
						v += 2;
				}
			}

			// Chinese Traditional
			category = "cht";
			arr = cjkCHT;
			offset = char.MaxValue - arr.Length;
			s = doc.SelectSingleNode ("/ldml/collations/collation[@type='stroke']/rules/pc").InnerText;
			v = 0x8002;
			foreach (char c in s) {
				if (c < '\u4E00')
					Console.Error.WriteLine ("---- warning: for {0} {1:X04} is omitted which should be {2:X04}", category, (int) c, v);
				else {
					arr [(int) c - offset] = (ushort) v++;
					if (v % 256 == 0)
						v += 2;
				}
			}

			// Japanese
			category = "ja";
			arr = cjkJA;
			offset = char.MaxValue - arr.Length;
			doc.Load (jaXML);
			s = doc.SelectSingleNode ("/ldml/collations/collation/rules/pc").InnerText;
			v = 0x8008;
			foreach (char c in s) {
				if (c < '\u4E00')
					Console.Error.WriteLine ("---- warning: for {0} {1:X04} is omitted which should be {2:X04}", category, (int) c, v);
				else {
					arr [(int) c - offset] = (ushort) v++;
					if (v % 256 == 0)
						v += 2;
				}
			}

			// Korean
			// Korean weight is somewhat complex. It first shifts
			// Hangul category from 52-x to 80-x (they are anyways
			// computed). CJK ideographs are placed at secondary
			// weight, like XX YY 01 zz 01, where XX and YY are
			// corresponding "reset" value and zz is 41,43,45...
			//
			// Unlike chs,cht and ja, Korean value is a combined
			// ushort which is computed as category
			//
			category = "ko";
			arr = cjkKO;
			offset = char.MaxValue - arr.Length;
			doc.Load (koXML);
			foreach (XmlElement reset in doc.SelectNodes ("/ldml/collations/collation/rules/reset")) {
				XmlElement sc = (XmlElement) reset.NextSibling;
				char rc = reset.InnerText [0];
				int ri = ((int) rc - 0xAC00) + 1;
				// compute "category" and "level 1"
				ushort p = (ushort)
					((ri / 254) * 256 + (ri % 254) + 2);
				s = sc.InnerText;
				v = 0x41;
				foreach (char c in s) {
					arr [(int) c - offset] = p;
					cjkKOlv2 [(int) c - offset] = (byte) v;
					v += 2;
				}
			}
		}

		#endregion

		#region Generate

		void InterpretParsedData ()
		{
			// number, secondary weights
			byte weight = 0x38;
			int [] numarr = numberSecondaryWeightBounds;
			for (int i = 0; i < numarr.Length; i += 2, weight++)
				for (int cp = numarr [i]; cp < numarr [i + 1]; cp++)
					if (Char.IsNumber ((char) cp))
						diacritical [cp] = weight;
		}

		void Generate ()
		{
			UnicodeCategory uc;

			#region Specially ignored // 01
			// This will raise "Defined" flag up.
			foreach (char c in specialIgnore)
				map [(int) c] = new CharMapEntry (0, 0, 0);
			#endregion


			#region Variable weights
			// Controls : 06 03 - 06 3D
			fillIndex [6] = 3;
			for (int i = 0; i < 65536; i++) {
				char c = (char) i;
				uc = Char.GetUnicodeCategory (c);
				if (uc == UnicodeCategory.Control &&
					!Char.IsWhiteSpace (c))
					AddCharMap (c, 6, 1);
			}

			// Apostrophe 06 80
			map ['\''] = new CharMapEntry (6, 80, 1);
			map ['\uFF63'] = new CharMapEntry (6, 80, 1); // full

			// Hyphen/Dash : 06 81 - 06 90
			fillIndex [6] = 0x81;
			for (int i = 0; i < 65536; i++) {
				if (Char.GetUnicodeCategory ((char) i)
					== UnicodeCategory.DashPunctuation)
					AddCharMapGroup ((char) i, 6, true, 1);
			}

			// Arabic variable weight chars 06 A0 -
			fillIndex [6] = 0xA0;
			// vowels
			for (int i = 0x64B; i <= 0x650; i++)
				AddCharMapGroup ((char) i, 6, true, 1);
			// sukun
			AddCharMapGroup ('\u0652', 6, false, 1);
			// shadda
			AddCharMapGroup ('\u0651', 6, false, 1);
			#endregion


			#region Nonspacing marks // 01
			// FIXME: 01 03 - 01 B6 ... annoyance :(

			// Combining diacritical marks: 01 DC -

			// LAMESPEC: It should not stop at '\u20E1'. There are
			// a few more characters (that however results in 
			// overflow of level 2 unless we start before 0xDD).
			fillIndex [0x1] = 0xDC;
			for (int i = 0x20d0; i <= 0x20e1; i++)
				AddCharMap ((char) i, 0x1, 1);
			#endregion


			#region Whitespaces // 07 03 -
			fillIndex [0x7] = 0x3;
			AddCharMapGroup (' ', 0x7, false, 1);
			AddCharMap ('\u00A0', 0x7, 1);
			for (int i = 9; i <= 0xD; i++)
				AddCharMap ((char) i, 0x7, 1);
			for (int i = 0x2000; i <= 0x200B; i++)
				AddCharMap ((char) i, 0x7, 1);
			AddCharMapGroup ('\u2028', 0x7, false, 1);
			AddCharMapGroup ('\u2029', 0x7, false, 1);

			// LAMESPEC: Windows developers seem to have thought 
			// that those characters are kind of whitespaces,
			// while they aren't.
			AddCharMapGroup ('\u2422', 0x7, false, 1); // blank symbol
			AddCharMapGroup ('\u2423', 0x7, false, 1); // open box
			#endregion


			#region ASCII non-alphanumeric // 07
			// non-alphanumeric ASCII except for: + - < = > '
			for (int i = 0x21; i < 0x7F; i++) {
				if (Char.IsLetterOrDigit ((char) i)
					|| "+-<=>'".IndexOf ((char) i) >= 0)
					continue; // they are not added here.
				AddCharMapGroup ((char) i, 0x7, false, 1);
			}
			#endregion


			// FIXME: for 07 xx we need more love.

			// FIXME: implement 08

			// FIXME: implement 09

			// FIXME: implement 0A

			#region Numbers // 0C 02 - 0C E1
			fillIndex [0xC] = 2;

			// 9F8 : Bengali "one less than the denominator"
			AddCharMap ('\u09F8', 0xC, 1);

			ArrayList numbers = new ArrayList ();
			for (int i = 0; i < 65536; i++)
				if (Char.IsNumber ((char) i))
					numbers.Add (i);

			ArrayList numberValues = new ArrayList ();
			foreach (int i in numbers)
				numberValues.Add (new DictionaryEntry (i, decimalValue [(char) i]));
			numberValues.Sort (DictionaryValueComparer.Instance);
			decimal prevValue = -1;
			foreach (DictionaryEntry de in numberValues) {
				decimal currValue = (decimal) de.Value;
				if (prevValue < currValue) {
					prevValue = currValue;
					fillIndex [0xC] += 1;
				}
				int cp = (int) de.Key;
				AddCharMap ((char) cp, 0xC, 1, diacritical [cp]);
			}

			// 221E: infinity
			fillIndex [0xC] = 0xFF;
			AddCharMap ('\u221E', 0xC, 1);
			#endregion

			#region Latin alphabets
			for (int i = 0; i < alphabets.Length; i++) {
				AddAlphaMap (alphabets [i], 0xE, alphaWeights [i]);
			}
			#endregion

			#region Letters (general)

			// Greek and Coptic
			fillIndex [0xF] = 02;
			for (int i = 0x0380; i < 0x03CF; i++)
				if (Char.IsLetter ((char) i))
					AddLetterMap ((char) i, 0xF, 1);
			fillIndex [0xF] = 0x40;
			for (int i = 0x03D0; i < 0x0400; i++)
				if (Char.IsLetter ((char) i))
					AddLetterMap ((char) i, 0xF, 1);

			// Cyrillic - UCA order w/ some modification
			fillIndex [0x10] = 0x3;
			// table which is moslty from UCA DUCET.
			for (int i = 0; i < orderedCyrillic.Length; i++) {
				char c = orderedCyrillic [i];
				if (Char.IsLetter (c))
					AddLetterMap (c, 0x10, 3);
			}
			for (int i = 0x0460; i < 0x0481; i++) {
				if (Char.IsLetter ((char) i))
					AddLetterMap ((char) i, 0x10, 3);
			}

			// Armenian
			fillIndex [0x11] = 0x3;
			for (int i = 0x0531; i < 0x0586; i++)
				if (Char.IsLetter ((char) i))
					AddLetterMap ((char) i, 0x11, 1);

			// Hebrew
			fillIndex [0x12] = 0x3;
			for (int i = 0x05D0; i < 0x05FF; i++)
				if (Char.IsLetter ((char) i))
					AddLetterMap ((char) i, 0x12, 1);

			// Arabic
			fillIndex [0x13] = 0x3;
			for (int i = 0x0621; i <= 0x064A; i++) {
				// Abjad
				if (Char.GetUnicodeCategory ((char) i)
					!= UnicodeCategory.OtherLetter)
					continue;
				map [i] = new CharMapEntry (0x13,
					(byte) arabicLetterPrimaryValues [i], 1);
			}
			fillIndex [0x13] = 0x84;
			for (int i = 0x0674; i < 0x06D6; i++)
				if (Char.IsLetter ((char) i))
					AddLetterMap ((char) i, 0x13, 1);

			// Devanagari
			for (int i = 0x0901; i < 0x0905; i++)
				if (Char.IsLetter ((char) i))
					AddLetterMap ((char) i, 0x14, 2);
			for (int i = 0x0905; i < 0x093A; i++)
				if (Char.IsLetter ((char) i))
					AddLetterMap ((char) i, 0x14, 4);
			for (int i = 0x093E; i < 0x094F; i++)
				if (Char.IsLetter ((char) i))
					AddLetterMap ((char) i, 0x14, 2);

			// Bengali
			fillIndex [0x15] = 02;
			for (int i = 0x0980; i < 0x9FF; i++) {
				if (i == 0x09E0)
					fillIndex [0x15] = 0x3B;
				switch (Char.GetUnicodeCategory ((char) i)) {
				case UnicodeCategory.NonSpacingMark:
				case UnicodeCategory.DecimalDigitNumber:
				case UnicodeCategory.OtherNumber:
					continue;
				}
				AddLetterMap ((char) i, 0x15, 1);
			}

			// Gurmukhi. orderedGurmukhi is from UCA
			fillIndex [0x16] = 02;
			for (int i = 0; i < orderedGurmukhi.Length; i++) {
				char c = orderedGurmukhi [i];
				if (c == '\u0A3C' || c == '\u0A4D' ||
					'\u0A66' <= c && c <= '\u0A71')
					continue;
				AddLetterMap (c, 0x16, 4);
			}

			// Gujarati. orderedGujarati is from UCA
			fillIndex [0x17] = 02;
			for (int i = 0; i < orderedGujarati.Length; i++)
				AddLetterMap (orderedGujarati [i], 0x17, 4);

			// Oriya
			fillIndex [0x18] = 02;
			for (int i = 0x0B00; i < 0x0B7F; i++) {
				switch (Char.GetUnicodeCategory ((char) i)) {
				case UnicodeCategory.NonSpacingMark:
				case UnicodeCategory.DecimalDigitNumber:
					continue;
				}
				AddLetterMap ((char) i, 0x18, 1);
			}

			// Tamil
			fillIndex [0x19] = 2;
			AddCharMap ('\u0BD7', 0x19, 0);
			fillIndex [0x19] = 0xA;
			// vowels
			for (int i = 0x0BD7; i < 0x0B94; i++)
				if (Char.IsLetter ((char) i))
					AddCharMap ((char) i, 0x19, 2);
			// special vowel
			fillIndex [0x19] = 0x24;
			AddCharMap ('\u0B94', 0x19, 0);
			fillIndex [0x19] = 0x26;
			// The array for Tamil consonants is a constant.
			// Windows have almost similar sequence to TAM from
			// tamilnet but a bit different in Grantha.
			for (int i = 0; i < orderedTamilConsonants.Length; i++)
				AddLetterMap (orderedTamilConsonants [i], 0x19, 4);
			// combining marks
			fillIndex [0x19] = 0x82;
			for (int i = 0x0BBE; i < 0x0BCD; i++)
				if (Char.GetUnicodeCategory ((char) i) ==
					UnicodeCategory.SpacingCombiningMark
					|| i == 0x0BC0)
					AddLetterMap ((char) i, 0x19, 2);

			// Telugu
			fillIndex [0x1A] = 0x4;
			for (int i = 0x0C00; i < 0x0C62; i++) {
				if (i == 0x0C55 || i == 0x0C56)
					continue; // skip
				AddCharMap ((char) i, 0x1A, 3);
				char supp = (i == 0x0C0B) ? '\u0C60':
					i == 0x0C0C ? '\u0C61' : char.MinValue;
				if (supp == char.MinValue)
					continue;
				AddCharMap (supp, 0x1A, 3);
			}

			// Kannada
			fillIndex [0x1B] = 4;
			for (int i = 0x0C80; i < 0x0CE5; i++) {
				if (i == 0x0CD5 || i == 0x0CD6)
					continue; // ignore
				AddCharMap ((char) i, 0x1B, 3);
			}
			
			// Malayalam
			fillIndex [0x1C] = 2;
			for (int i = 0x0D02; i < 0x0D61; i++)
				// FIXME: I avoided MSCompatUnicodeTable usage
				// here (it results in recursion). So check if
				// using NonSpacingMark makes sense or not.
				if (Char.GetUnicodeCategory ((char) i) != UnicodeCategory.NonSpacingMark)
//				if (!MSCompatUnicodeTable.IsIgnorable ((char) i))
					AddCharMap ((char) i, 0x1C, 1);

			// Thai ... note that it breaks 0x1E wall after E2B!
			// Also, all Thai characters have level 2 value 3.
			fillIndex [0x1E] = 2;
			for (int i = 0xE44; i < 0xE48; i++)
				AddCharMap ((char) i, 0x1E, 1, 3);
			for (int i = 0xE01; i < 0xE2B; i++)
				AddCharMap ((char) i, 0x1E, 6, 0);
			fillIndex [0x1F] = 5;
			for (int i = 0xE2B; i < 0xE30; i++)
				AddCharMap ((char) i, 0x1F, 6, 0);
			for (int i = 0xE30; i < 0xE3B; i++)
				AddCharMap ((char) i, 0x1F, 1, 3);
			// some Thai characters remains.
			char [] specialThai = new char [] {'\u0E45', '\u0E46',
				'\u0E4E', '\u0E4F', '\u0E5A', '\u0E5B'};
			foreach (char c in specialThai)
				AddCharMap (c, 0x1F, 1);

			// Lao
			fillIndex [0x1F] = 2;
			for (int i = 0xE80; i < 0xEDF; i++)
				if (Char.IsLetter ((char) i))
					AddCharMap ((char) i, 0x1F, 1);

			// Georgian. orderedGeorgian is from UCA DUCET.
			fillIndex [0x21] = 5;
			for (int i = 0; i < orderedGeorgian.Length; i++)
				AddLetterMap (orderedGeorgian [i], 0x21, 5);

			// FIXME: Japanese Kana needs constant array.

			// JIS Japanese square chars.
			fillIndex [0x22] = 0x97;
			jisJapanese.Sort (JISComparer.Instance);
			foreach (JISCharacter j in jisJapanese)
				AddCharMap ((char) j.CP, 0x22, 1);
			// non-JIS Japanese square chars.
			nonJisJapanese.Sort (NonJISComparer.Instance);
			foreach (NonJISCharacter j in nonJisJapanese)
				AddCharMap ((char) j.CP, 0x22, 1);

			// Bopomofo
			fillIndex [0x23] = 0x02;
			for (int i = 0x3105; i <= 0x312C; i++)
				AddCharMap ((char) i, 0x23, 1);

			// Estrangela: ancient Syriac
			fillIndex [0x24] = 0x0B;
			ArrayList syriacAlternatives = new ArrayList (
				new int [] {0x714, 0x716, 0x71C, 0x724, 0x727});
			for (int i = 0x0710; i <= 0x072C; i++)
				if (i != 0x0711) // ignored
					AddCharMap ((char) i, 0x24,
						syriacAlternatives.Contains (i) ?
						(byte) 2 : (byte) 4);

			// Thaana
			fillIndex [0x24] = 0x6E;
			for (int i = 0; i < orderedThaana.Length; i++)
				AddCharMap (orderedThaana [i], 0x24, 2);
			#endregion

			#region Level2 adjustment
			// Arabic Hamzah
			diacritical [0x624] = 0x5;
			diacritical [0x626] = 0x7;
			diacritical [0x622] = 0x9;
			diacritical [0x623] = 0xA;
			diacritical [0x625] = 0xB;
			diacritical [0x649] = 0x5; // 'alif maqs.uurah
			diacritical [0x64A] = 0x7; // Yaa'


			for (int i = 0; i < 0x10000; i++) {
				switch (map [i].Category) {
				case 0xE: // Latin diacritics
					map [i] = new CharMapEntry (0xE, map [i].Level1, diacritical [i]);
					break;
				case 0x13: // Arabic
					if (diacritical [i] == 0)
						// default by 8
						diacritical [i] = 0x8;
					map [i] = new CharMapEntry (0xE, map [i].Level1, diacritical [i]);
					break;
				}
			}
			#endregion

			// FIXME: Add more culture-specific letters (that are
			// not supported in Windows collation) here.

			// Surrogate : computed.

			// FIXME: Hangul.

			// FIXME: CJK.

			// PrivateUse : computed.
			// remaining Surrogate : computed.

			// FIXME: CJK Extensions goes here.

			#region Special "biggest" area (FF FF)
			fillIndex [0xFF] = 0xFF;
			char [] specialBiggest = new char [] {
				'\u3005', '\u3031', '\u3032', '\u309D',
				'\u309E', '\u30FC', '\u30FD', '\u30FE',
				'\uFE7C', '\uFE7D', '\uFF70'};
			foreach (char c in specialBiggest)
				AddCharMap (c, 0xFF, 0);
			#endregion

		}

		// Reset fillIndex to fixed value and call AddLetterMap().
		private void AddAlphaMap (char c, byte category, byte alphaWeight)
		{
			fillIndex [category] = alphaWeight;
			AddLetterMap (c, category, 0);
		}

		private void AddLetterMap (char c, byte category, byte updateCount)
		{
			char c2;

			// process lowerletter recursively (if not defined).
			c2 = Char.ToLower (c, CultureInfo.InvariantCulture);
			if (c2 != c && !map [(int) c2].Defined)
				AddLetterMap (c2, category, updateCount);

			// <small> updates index
			c2 = ToSmallForm (c);
			if (c2 != c)
				AddCharMap (c2, category, updateCount);
			// itself
			AddCharMap (c, category, updateCount);
			// <full>
			c2 = ToFullWidth (c);
			if (c2 != c)
				AddLetterMap (c2, category, 0);

			// FIXME: implement decorated characters w/ diacritical
			// marks.

			// process upperletter recursively (if not defined).
			c2 = Char.ToUpper (c, CultureInfo.InvariantCulture);
			if (c2 != c && !map [(int) c2].Defined)
				AddLetterMap (c2, category, updateCount);
		}

		private void AddCharMap (char c, byte category, byte increment)
		{
			AddCharMap (c, category, increment, 1);
		}
		
		private void AddCharMap (char c, byte category, byte increment, byte level2)
		{
			map [(int) c] = new CharMapEntry (category,
				category == 1 ? level2 : fillIndex [category],
				category != 1 ? fillIndex [category] : level2);
			fillIndex [category] += increment;
		}

		private void AddCharMapGroup (char c, byte category, bool tail, byte updateCount)
		{
			// <small> updates index
			char c2 = tail ?
				ToSmallFormTail (c) :
				ToSmallForm (c);
			if (c2 != c)
				AddCharMap (c2, category, updateCount);
			// itself
			AddCharMap (c, category, updateCount);
			// <full>
			c2 = tail ?
				ToFullWidthTail (c) :
				ToFullWidth (c);
			if (c2 != c)
				AddCharMapGroup (c2, category, tail, 0);
			// FIXME: add more
		}

		char ToFullWidth (char c)
		{
			return ToDecomposed (c, DecompositionFull, false);
		}

		char ToFullWidthTail (char c)
		{
			return ToDecomposed (c, DecompositionFull, true);
		}

		char ToSmallForm (char c)
		{
			return ToDecomposed (c, DecompositionSmall, false);
		}

		char ToSmallFormTail (char c)
		{
			return ToDecomposed (c, DecompositionSmall, true);
		}

		char ToDecomposed (char c, byte d, bool tail)
		{
			if (decompType [(int) c] != d)
				return c;
			int idx = decompIndex [(int) c];
			if (tail)
				idx += decompLength [(int) c] - 1;
			return (char) decompValues [idx];
		}

		bool ExistsJIS (int cp)
		{
			foreach (JISCharacter j in jisJapanese)
				if (j.CP == cp)
					return true;
			return false;
		}

		#endregion

		#region Level 3 properties (Case/Width)

		private byte ComputeLevel3WeightRaw (char c) // add 2 for sortkey value
		{
			// Korean
			if ('\u1100' <= c && c <= '\u11F9')
				return 2;
			if ('\uFFA0' <= c && c <= '\uFFDC')
				return 4;
			if ('\u3130' <= c && c <= '\u3164')
				return 5;
			// numbers
			if ('\u2776' <= c && c <= '\u277F')
				return 4;
			if ('\u2780' <= c && c <= '\u2789')
				return 8;
			if ('\u2776' <= c && c <= '\u2793')
				return 0xC;
			if ('\u2160' <= c && c <= '\u216F')
				return 0x18;
			if ('\u2181' <= c && c <= '\u2182')
				return 0x18;
			// Arabic
			if ('\u2135' <= c && c <= '\u2138')
				return 4;
			if ('\uFE80' <= c && c < '\uFE8E') {
				// 2(Isolated)/8(Final)/0x18(Medial)
				switch (decompType [(int) c]) {
				case DecompositionIsolated:
					return 2;
				case DecompositionFinal:
					return 8;
				case DecompositionMedial:
					return 0x18;
				}
			}

			// actually I dunno the reason why they have weights.
			switch (c) {
			case '\u01BC':
				return 0x10;
			case '\u06A9':
				return 0x20;
			case '\u06AA':
				return 0x28;
			}

			byte ret = 0;
			switch (c) {
			case '\u03C2':
			case '\u2104':
			case '\u212B':
				ret |= 8;
				break;
			case '\uFE42':
				ret |= 0xC;
				break;
			}

			// misc
			switch (decompType [(int) c]) {
			case DecompositionFull: // <full>
			case DecompositionSub: // <sub>
			case DecompositionSuper: // <super>
				ret |= decompType [(int) c];
				break;
			}
			if (isSmallCapital [(int) c]) // grep "SMALL CAPITAL"
				ret |= 8;
			if (isUppercase [(int) c]) // DerivedCoreProperties
				ret |= 0x10;

			return ret;
		}

		// TODO: implement GetArabicFormInRepresentationD(),
		// GetNormalizationType(), IsSmallCapital() and IsUppercase().
		// (They can be easily to be generated.)

		#endregion
	}

	struct CharMapEntry
	{
		public byte Category;
		public byte Level1;
		public byte Level2; // It is always single byte.
		public bool Defined;

		public CharMapEntry (byte category, byte level1, byte level2)
		{
			Category = category;
			Level1 = level1;
			Level2 = level2;
			Defined = true;
		}
	}

	class JISCharacter
	{
		public readonly int CP;
		public readonly int JIS;

		public JISCharacter (int cp, int cpJIS)
		{
			CP = cp;
			JIS = cpJIS;
		}
	}

	class JISComparer : IComparer
	{
		public static readonly JISComparer Instance =
			new JISComparer ();

		public int Compare (object o1, object o2)
		{
			JISCharacter j1 = (JISCharacter) o1;
			JISCharacter j2 = (JISCharacter) o2;
			return j2.JIS - j1.JIS;
		}
	}

	class NonJISCharacter
	{
		public readonly int CP;
		public readonly string Name;

		public NonJISCharacter (int cp, string name)
		{
			CP = cp;
			Name = name;
		}
	}

	class NonJISComparer : IComparer
	{
		public static readonly NonJISComparer Instance =
			new NonJISComparer ();

		public int Compare (object o1, object o2)
		{
			NonJISCharacter j1 = (NonJISCharacter) o1;
			NonJISCharacter j2 = (NonJISCharacter) o2;
			return string.CompareOrdinal (j1.Name, j2.Name);
		}
	}

	class DictionaryValueComparer : IComparer
	{
		public static readonly DictionaryValueComparer Instance
			= new DictionaryValueComparer ();

		private DictionaryValueComparer ()
		{
		}

		public /*static*/ int Compare (object o1, object o2)
		{
			DictionaryEntry e1 = (DictionaryEntry) o1;
			DictionaryEntry e2 = (DictionaryEntry) o2;
			// FIXME: in case of 0, compare decomposition categories
			return Decimal.Compare ((decimal) e1.Value, (decimal) e2.Value);
		}
	}

	class UCAComparer : IComparer
	{
		public static readonly UCAComparer Instance
			= new UCAComparer ();

		private UCAComparer ()
		{
		}

		public int Compare (object o1, object o2)
		{
			char i1 = (char) o1;
			char i2 = (char) o2;

			int l1 = CollationElementTable.GetSortKeyCount (i1);
			int l2 = CollationElementTable.GetSortKeyCount (i2);
			int l = l1 > l2 ? l2 : l1;

			for (int i = 0; i < l; i++) {
				SortKeyValue k1 = CollationElementTable.GetSortKey (i1, i);
				SortKeyValue k2 = CollationElementTable.GetSortKey (i2, i);
				int v = k1.Primary.CompareTo (k2.Primary);
				if (v != 0)
					return v;
				v = k1.Secondary.CompareTo (k2.Secondary);
				if (v != 0)
					return v;
				v = k1.Thirtiary.CompareTo (k2.Thirtiary);
				if (v != 0)
					return v;
				v = k1.Quarternary.CompareTo (k2.Quarternary);
				if (v != 0)
					return v;
			}
			return l2 - l1;
		}
	}
}
