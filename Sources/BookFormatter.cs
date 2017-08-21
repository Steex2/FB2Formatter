﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;
using System.IO;
using System.Web;
using System.Globalization;


namespace FB2Formatter
{
	public class BookFormatter
	{
		private enum TextFormatMode
		{
			Structured,
			Inline,
			Preformatted,
		}


		private class BookNodeInfo
		{
			// Name of node tag without angle brackets. Example: "section"
			public string Name { get; set; }
			// Text processing mode at the current node and the default mode for all its subnodes.
			public TextFormatMode FormatMode { get; set; }
			// True if the node does not contain another nodes.
			public bool Empty { get; set; }

			public BookNodeInfo(string name, TextFormatMode formatMode)
			{
				Name = name;
				FormatMode = formatMode;
				Empty = true;
			}
		}

		private class BookNodeStack
		{
			private static readonly HashSet<String> inlineTextTags = new HashSet<String> {
				/*formatted text*/ "p", "v", "subtitle", "text-author", "th", "td",
				/*author*/         "first-name", "middle-name", "last-name", "nickname", "home-page", "email",
				/*title-info*/     "genre", "id", "book-title", "lang", "keywords", "date",
				/*document-info*/  "src-url", "src-ocr", "version", "program-used",
				/*publish-info*/   "book-name", "publisher", "city", "year", "isbn",
				/*custom-info*/    "custom-info"
			};

			private static readonly HashSet<String> preformattedTextTags = new HashSet<String> { "code" };

			private TextFormatMode defaultFormatMode;
			private Stack<BookNodeInfo> items;

			public int NodeLevel
			{
				get
				{
					return items.Count - 1;
				}
			}

			public string NodeName
			{
				get
				{
					return items.Count > 0 ? items.Peek().Name : null;
				}
			}

			public bool NodeEmpty
			{
				get
				{
					return items.Count > 0 ? items.Peek().Empty : true;
				}
			}

			public TextFormatMode FormatMode
			{
				get
				{
					return items.Count > 0 ? items.Peek().FormatMode : defaultFormatMode;
				}
			}


			public BookNodeStack(TextFormatMode defaultFormatMode)
			{
				this.defaultFormatMode = defaultFormatMode;
				items = new Stack<BookNodeInfo>();
			}

			public void AddNode(string name)
			{
				// The current item is not empty anymore.
				if (items.Any())
				{
					items.Peek().Empty = false;
				}

				// Determine the format mode.
				TextFormatMode formatMode;
				if (inlineTextTags.Contains(name))
				{
					formatMode = TextFormatMode.Inline;
				}
				else if (preformattedTextTags.Contains(name))
				{
					formatMode = TextFormatMode.Preformatted;
				}
				else
				{
					formatMode = TextFormatMode.Structured;
				}

				// Inner items cannot make the format mode more strict.
				if (formatMode < FormatMode)
				{
					formatMode = FormatMode;
				}

				// Add the new item.
				items.Push(new BookNodeInfo(name, formatMode));
			}

			public void DeleteNode(string name)
			{
				// Make sure the stack is not empty.
				if (!items.Any())
				{
					throw new Exception("Node stack is empty.");
				}

				// Make sure the opening and the closing tags correspond.
				if (items.Peek().Name != name)
				{
					throw new Exception(string.Format("Book node hierarchy is broken: open node: \"{0}\", closing node \"{1}\".", items.Peek().Name, name));
				}

				// Remove the item.
				items.Pop();
			}
		}


		private static readonly char indentSymbol = ' ';
		private static readonly int indentLength = 1;
		private static readonly int binaryLineSize = 80;


		public BookFormatter()
		{
		}

		public void FormatBook(string sourceFile, string targetFile)
		{
			BookNodeStack nodeStack = new BookNodeStack(TextFormatMode.Structured);
			Encoding encoding = Encoding.UTF8;
			StringBuilder output = new StringBuilder();
			bool allowWhitespace = false;
			bool binaryElement = false;

			using (XmlTextReader reader = new XmlTextReader(sourceFile))
			{
				while (reader.Read())
				{
					//output.AppendLine(String.Format("{}", ))
					switch (reader.NodeType)
					{
						case XmlNodeType.XmlDeclaration:
							encoding = Encoding.GetEncoding(reader["encoding"]);
							WriteDeclaration(output, reader.Value);
							break;

						case XmlNodeType.Element:
							string elementName = reader.Name;
							bool elementEmpty = reader.IsEmptyElement;

							WriteElementOpeningTag(output, elementName, nodeStack.FormatMode, nodeStack.NodeLevel + 1);
							WriteElementAttributes(output, reader);

							if (elementEmpty)
							{
								WriteEmptyElementCloser(output);
							}
							else
							{
								WriteElementCloser(output);
							}

							allowWhitespace = allowWhitespace && nodeStack.FormatMode != TextFormatMode.Structured;

							if (!elementEmpty)
							{
								nodeStack.AddNode(elementName);
							}

							binaryElement = (elementName == "binary" && !elementEmpty);
							break;

						case XmlNodeType.EndElement:
							TextFormatMode insideFormatMode = nodeStack.FormatMode;
							nodeStack.DeleteNode(reader.Name);
							TextFormatMode outsideFormatMode = nodeStack.FormatMode;

							if (insideFormatMode == TextFormatMode.Inline &&
								outsideFormatMode == TextFormatMode.Structured)
							{
								DeleteTrailingWhitespace(output);
							}

							WriteElementClosingTag(output, reader.Name, insideFormatMode, nodeStack.NodeLevel + 1);
							allowWhitespace = allowWhitespace && nodeStack.FormatMode != TextFormatMode.Structured;
							binaryElement = false;
							break;

						case XmlNodeType.Whitespace:
						case XmlNodeType.SignificantWhitespace:
							if (nodeStack.FormatMode != TextFormatMode.Structured)
							{
								WriteText(output, reader.Value, nodeStack.FormatMode, ref allowWhitespace);
							}
							break;

						case XmlNodeType.Text:
							if (binaryElement)
							{
								WriteBinary(output, reader.Value, nodeStack.NodeLevel + 1);
							}
							else
							{
								WriteText(output, reader.Value, nodeStack.FormatMode, ref allowWhitespace);
							}
							break;
					}
				}
			}

			//
			File.WriteAllText(targetFile + ".txt", output.ToString(), encoding);
		}


		private void WriteDeclaration(StringBuilder output, string content)
		{
			output.AppendFormat("<?xml {0}?>", content);
		}

		private void WriteElementOpeningTag(StringBuilder output, string name, TextFormatMode formatMode, int level)
		{
			if (formatMode == TextFormatMode.Structured)
			{
				output.AppendLine();
				output.Append(new string(indentSymbol, indentLength * level));
			}

			output.AppendFormat("<{0}", name);
		}

		private void WriteElementClosingTag(StringBuilder output, string name, TextFormatMode formatMode, int level)
		{
			if (formatMode == TextFormatMode.Structured)
			{
				output.AppendLine();
				output.Append(new string(indentSymbol, indentLength * level));
			}

			output.AppendFormat("</{0}>", name);
		}

		private void WriteEmptyElementCloser(StringBuilder output)
		{
			output.Append("/>");
		}

		private void WriteElementCloser(StringBuilder output)
		{
			output.Append(">");
		}

		private void WriteElementAttributes(StringBuilder output, XmlTextReader reader)
		{
			while (reader.MoveToNextAttribute())
			{
				output.AppendFormat(" {0}=\"{1}\"", reader.Name, HttpUtility.HtmlEncode(reader.Value));
			}
		}

		private void WriteText(StringBuilder output, string text, TextFormatMode formatMode, ref bool allowWhitespace)
		{
			switch (formatMode)
			{
				case TextFormatMode.Structured:
					throw new Exception("Cannot write text in the current context.");

				case TextFormatMode.Inline:
					WriteInlineText(output, text, ref allowWhitespace);
					break;

				case TextFormatMode.Preformatted:
					output.Append(text);
					allowWhitespace = true;
					break;
			}
		}

		private void WriteInlineText(StringBuilder output, string text, ref bool allowWhitespace)
		{
			foreach (char chr in text)
			{
				UnicodeCategory charCategory = char.GetUnicodeCategory(chr);

				// Skip control symbols
				if (charCategory == UnicodeCategory.Control)
				{
					continue;
				}

				// Replace all simple whitespaces and line separators with single whitespace.
				// Other symbols (including special whitespaces) are written as is.
				bool isWhitespace = 
					chr == ' ' ||
					charCategory == UnicodeCategory.LineSeparator;

				if (!isWhitespace)
				{
					output.Append(chr);
					allowWhitespace = true;
				}
				else if (allowWhitespace)
				{
					output.Append(' ');
					allowWhitespace = false;
				}
			}
		}

		private void WriteBinary(StringBuilder output, string content, int level)
		{
			byte[] binaryContent = Convert.FromBase64String(content);
			foreach (string chunk in Utils.SplitStringBy(Convert.ToBase64String(binaryContent), binaryLineSize))
			{
				output.AppendLine();
				output.Append(new string(indentSymbol, indentLength * level));
				output.Append(chunk);
			}
		}

		private void DeleteTrailingWhitespace(StringBuilder output)
		{
			if (output.Length > 0 && output[output.Length - 1] == ' ')
			{
				output.Length -= 1;
			}
		}

	}
}
