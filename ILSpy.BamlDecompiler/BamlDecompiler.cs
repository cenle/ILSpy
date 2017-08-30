﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Mono.Cecil;
using Ricciolo.StylesExplorer.MarkupReflection;

namespace ILSpy.BamlDecompiler
{
	public static class BamlDecompiler
	{
		public static XDocument LoadIntoDocument(IAssemblyResolver resolver, AssemblyDefinition asm, Stream stream)
		{
			XDocument xamlDocument;
			using (XmlBamlReader reader = new XmlBamlReader(stream, new CecilTypeResolver(resolver, asm))) {
				xamlDocument = XDocument.Load(reader);
				ConvertConnectionIds(xamlDocument, asm);
				ConvertToEmptyElements(xamlDocument.Root);
				MoveNamespacesToRoot(xamlDocument, reader.XmlnsDefinitions);
				return xamlDocument;
			}
		}

		static void ConvertConnectionIds(XDocument xamlDocument, AssemblyDefinition asm)
		{
			var attr = xamlDocument.Root.Attribute(XName.Get("Class", XmlBamlReader.XWPFNamespace));
			if (attr != null) {
				string fullTypeName = attr.Value;
				var mappings = new ConnectMethodDecompiler(asm).DecompileEventMappings(fullTypeName);
				RemoveConnectionIds(xamlDocument.Root, mappings);
			}
		}

		class XAttributeComparer : IEqualityComparer<XAttribute>
		{
			public bool Equals(XAttribute x, XAttribute y)
			{
				if (ReferenceEquals(x, y))
					return true;
				if (x == null || y == null)
					return false;
				return x.ToString() == y.ToString();
			}

			public int GetHashCode(XAttribute obj)
			{
				return obj.ToString().GetHashCode();
			}
		}

		static void MoveNamespacesToRoot(XDocument xamlDocument, IEnumerable<XmlNamespace> missingXmlns)
		{
			var additionalXmlns = new HashSet<XAttribute>(new XAttributeComparer()) {
				new XAttribute("xmlns", XmlBamlReader.DefaultWPFNamespace),
				new XAttribute(XName.Get("x", XNamespace.Xmlns.NamespaceName), XmlBamlReader.XWPFNamespace)
			};

			additionalXmlns.AddRange(
				missingXmlns
					.Where(ns => !string.IsNullOrWhiteSpace(ns.Prefix))
					.Select(ns => new XAttribute(XName.Get(ns.Prefix, XNamespace.Xmlns.NamespaceName), ns.Namespace))
			);

			foreach (var element in xamlDocument.Root.DescendantsAndSelf()) {
				if (element.Name.NamespaceName != XmlBamlReader.DefaultWPFNamespace && !additionalXmlns.Any(ka => ka.Value == element.Name.NamespaceName)) {
					string newPrefix = new string(element.Name.LocalName.Where(c => char.IsUpper(c)).ToArray()).ToLowerInvariant();
					int current = additionalXmlns.Count(ka => ka.Name.Namespace == XNamespace.Xmlns && ka.Name.LocalName.TrimEnd(ch => char.IsNumber(ch)) == newPrefix);
					if (current > 0)
						newPrefix += (current + 1).ToString();
					XName defaultXmlns = XName.Get(newPrefix, XNamespace.Xmlns.NamespaceName);
					if (element.Name.NamespaceName != XmlBamlReader.DefaultWPFNamespace)
						additionalXmlns.Add(new XAttribute(defaultXmlns, element.Name.NamespaceName));
				}
			}

			foreach (var xmlns in additionalXmlns.Except(xamlDocument.Root.Attributes())) {
				xamlDocument.Root.Add(xmlns);
			}
		}

		static void ConvertToEmptyElements(XElement element)
		{
			foreach (var el in element.Elements()) {
				if (!el.IsEmpty && !el.HasElements && el.Value == "") {
					el.RemoveNodes();
					continue;
				}
				ConvertToEmptyElements(el);
			}
		}

		static void RemoveConnectionIds(XElement element, Dictionary<long, EventRegistration[]> eventMappings)
		{
			foreach (var child in element.Elements())
				RemoveConnectionIds(child, eventMappings);

			var removableAttrs = new List<XAttribute>();
			var addableAttrs = new List<XAttribute>();
			foreach (var attr in element.Attributes(XName.Get("ConnectionId", XmlBamlReader.XWPFNamespace))) {
				int id;
				if (int.TryParse(attr.Value, out id) && eventMappings.ContainsKey(id)) {
					var map = eventMappings[id];
					foreach (var entry in map) {
						string xmlns = ""; // TODO : implement xmlns resolver!
						addableAttrs.Add(new XAttribute(xmlns + entry.EventName, entry.MethodName));
					}
					removableAttrs.Add(attr);
				}
			}
			foreach (var attr in removableAttrs)
				attr.Remove();
			element.Add(addableAttrs);
		}
	}
}