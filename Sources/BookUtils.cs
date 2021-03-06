﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;
using System.IO;
using System.Web;
using System.Globalization;
using System.Windows.Forms;
using System.Text.RegularExpressions;


namespace FB2Formatter
{
	public static class BookUtils
	{
		private class BinaryElementInfo
		{
			// The first line of the element.
			public int StartLine { get; set; }
			// Position of the first symbol.
			public int StartOffset { get; set; }
			// The last line of the element.
			public int EndLine { get; set; }
			// Position _after_ the last symbol.
			public int EndOffset { get; set; }
			public string Name { get; set; }
			public string Format { get; set; }
			public byte[] Content { get; set; }
		}


		public static XmlDocument OpenBook(string path)
		{
			XmlDocument book = new XmlDocument();
			book.Load(path);
			return book;
		}

		public static IEnumerable<BookAttachment> EnumBookPictures(XmlDocument book)
		{
			foreach (XmlElement binary in book.DocumentElement.ChildNodes.OfType<XmlElement>().Where(e => e.Name == "binary"))
			{
				string contentType = binary.GetAttribute("content-type");
				if (contentType == "image/png" ||
					contentType == "image/jpeg")
				{
					yield return new BookAttachment(binary);
				}
			}
		}

		public static IEnumerable<BookAttachment> EnumFolderPictures(string folderPath)
		{
			foreach (string filePath in Directory.GetFiles(folderPath).OrderBy(n => n))
			{
				string extension = Path.GetExtension(filePath);
				if (extension == ".png" ||
					extension == ".jpg" ||
					extension == ".jpeg")
				{
					yield return new BookAttachment(filePath);
				}
			}
		}


		public static void FormatBook(string sourceFile, string targetFile)
		{
			try
			{
				BookFormatter formatter = new BookFormatter(sourceFile, targetFile);
				formatter.FormatBook();
			}
			catch (Exception ex)
			{
				MessageBox.Show(ex.Message, "FBF", MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
		}

		public static void FormatReferences(string sourceFile, string targetFile)
		{
			try
			{
				string content = File.ReadAllText(sourceFile);

				int noteIndex = 1;
				Regex anchorRegex = new Regex(@"\<a l\:href=""#n_[a-z0-9]+"" type=""note""\>\[[a-z0-9]+\]\</a\>");
				string anchorResult = anchorRegex.Replace(content, s => string.Format(@"<a l:href=""#n_{0}"" type=""note"">[{0}]</a>", noteIndex++));

				noteIndex = 1;
				Regex targetRegex = new Regex(@"\<section id=""n_[a-z0-9]+""\>(\s*)\<title\>(\s*)\<p\>[a-z0-9]+\</p\>");
				string targetResult = targetRegex.Replace(anchorResult, m =>
					string.Format(@"<section id=""n_{0}"">{1}<title>{2}<p>{0}</p>", noteIndex++, m.Groups[1].Value, m.Groups[2].Value));

				File.WriteAllText(targetFile, targetResult, Encoding.UTF8);
			}
			catch (Exception ex)
			{
				MessageBox.Show(ex.Message, "FBF", MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
		}

		public static void FormatBookPictures(string sourceFile, string targetFile)
		{
			// Collect binary items and determine encoding.
			Encoding encoding = null;
			List<BinaryElementInfo> binaries = new List<BinaryElementInfo>();
			BinaryElementInfo currentBinary = null;

			using (XmlTextReader reader = new XmlTextReader(sourceFile))
			{
				while (reader.Read())
				{
					switch (reader.NodeType)
					{
						case XmlNodeType.XmlDeclaration:
							encoding = Encoding.GetEncoding(reader["encoding"]);
							break;
						case XmlNodeType.Element:
							if (reader.Name == "binary")
							{
								currentBinary = new BinaryElementInfo();
								currentBinary.StartLine = reader.LineNumber - 1;
								currentBinary.StartOffset = reader.LinePosition - 1 - 1; // consider starting "<" symbol
								currentBinary.Name = reader["id"];
								currentBinary.Format = reader["content-type"];
							}
							break;
						case XmlNodeType.EndElement:
							if (reader.Name == "binary")
							{
								currentBinary.EndLine = reader.LineNumber - 1;
								currentBinary.EndOffset = reader.LinePosition + 7 - 1; // consider "binary>" symbols after "</" sequence
								binaries.Add(currentBinary);
							}
							break;
						case XmlNodeType.Text:
							if (currentBinary != null)
							{
								currentBinary.Content = Convert.FromBase64String(reader.Value);
							}
							break;
					}
				}
			}

			// Read the book as a text file and replace binary content.
			string[] lines = File.ReadAllLines(sourceFile, encoding);

			using (StreamWriter writer = new StreamWriter(targetFile, false, encoding))
			{
				int currentLine = 0;
				int currentChar = 0;

				foreach (BinaryElementInfo binary in binaries)
				{
					// Write all preceding lines
					while (currentLine < binary.StartLine)
					{
						if (currentChar > 0)
						{
							string remainder = lines[currentLine].Substring(currentChar);
							if (!string.IsNullOrWhiteSpace(remainder))
							{
								writer.WriteLine(remainder.Trim());
							}
						}
						else
						{
							writer.WriteLine(lines[currentLine]);
						}

						++currentLine;
						currentChar = 0;
					}

					// Write significant preceding characters.
					if (binary.StartOffset > currentChar)
					{
						string inclusion = lines[currentLine].Substring(currentChar, binary.StartOffset - currentChar);
						if (!string.IsNullOrWhiteSpace(inclusion))
						{
							writer.WriteLine(inclusion.TrimEnd());
						}
					}

					// Write binary element
					writer.WriteLine(" <binary id=\"{1}\" content-type=\"{0}\">", binary.Format, binary.Name);
					foreach (string chunk in Utils.SplitStringBy(Convert.ToBase64String(binary.Content), Config.Main.BinaryLineSize))
					{
						writer.WriteLine("  " + chunk);
					}
					writer.WriteLine(" </binary>");

					currentLine = binary.EndLine;
					currentChar = binary.EndOffset;
				}

				// Write the possible line remainder.
				if (currentChar > 0)
				{
					string remainder = lines[currentLine].Substring(currentChar);
					if (!string.IsNullOrWhiteSpace(remainder))
					{
						writer.WriteLine(remainder.TrimStart());
					}

					++currentLine;
					currentChar = 0;
				}

				// Write the remaining lines.
				while (currentLine < lines.Length)
				{
					writer.WriteLine(lines[currentLine]);
					++currentLine;
				}
			}
		}

		public static void ExtractPicturesToFiles(string sourceFile, string targetFolder)
		{
			XmlDocument book = OpenBook(sourceFile);

			Directory.CreateDirectory(targetFolder);

			foreach (BookAttachment picture in EnumBookPictures(book))
			{
				File.WriteAllBytes(Path.Combine(targetFolder, picture.FileName), picture.Content);
			}
		}

		public static void ExtractPicturesToXml(string sourceFile, string targetFile)
		{
			XmlDocument book = OpenBook(sourceFile);

			Encoding encoding = Encoding.GetEncoding((book.FirstChild as XmlDeclaration).Encoding);
			using (StreamWriter wr = new StreamWriter(targetFile, false, encoding))
			{
				foreach (BookAttachment picture in EnumBookPictures(book))
				{
					wr.WriteLine(" <binary id=\"{1}\" content-type=\"{0}\">", picture.Format, picture.Name);

					foreach (string chunk in Utils.SplitStringBy(Convert.ToBase64String(picture.Content), Config.Main.BinaryLineSize))
					{
						wr.WriteLine("  " + chunk);
					}

					wr.WriteLine(" </binary>");
				}
			}
		}

		public static void ConvertFolderPicturesToXml(string sourceFolder, string targetFile)
		{
			using (StreamWriter wr = new StreamWriter(targetFile, false, Encoding.ASCII))
			{
				foreach (BookAttachment picture in EnumFolderPictures(sourceFolder))
				{
					wr.WriteLine(" <binary id=\"{1}\" content-type=\"{0}\">", picture.Format, picture.Name);

					foreach (string chunk in Utils.SplitStringBy(Convert.ToBase64String(picture.Content), Config.Main.BinaryLineSize))
					{
						wr.WriteLine("  " + chunk);
					}

					wr.WriteLine(" </binary>");
				}
			}
		}

		public static void ConvertListPicturesToXml(IEnumerable<string> sourceFiles, string targetFile)
		{
			using (StreamWriter wr = new StreamWriter(targetFile, false, Encoding.ASCII))
			{
				foreach (string file in sourceFiles)
				{
					BookAttachment picture = new BookAttachment(file);

					wr.WriteLine(" <binary id=\"{1}\" content-type=\"{0}\">", picture.Format, picture.Name);

					foreach (string chunk in Utils.SplitStringBy(Convert.ToBase64String(picture.Content), Config.Main.BinaryLineSize))
					{
						wr.WriteLine("  " + chunk);
					}

					wr.WriteLine(" </binary>");
				}
			}
		}

	}
}
