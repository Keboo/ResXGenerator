﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Resources;
using System.Web;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace VocaDb.ResXFileCodeGenerator
{
	public sealed record GeneratorOptions(string LocalNamespace, string? CustomToolNamespace, string ClassName);

	public sealed class Generator : IDisposable
	{
		//private const string SystemGlobalization = $"{nameof(System)}.{nameof(System.Globalization)}";
		private const string SystemGlobalization = "System.Globalization";
		//private const string SystemResources = $"{nameof(System)}.{nameof(System.Resources)}";
		private const string SystemResources = "System.Resources";
		private const string AutoGeneratedHeader = @"// ------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
// ------------------------------------------------------------------------------

";

		private const string s_resourceManagerVariable = "s_resourceManager";
		private const string ResourceManagerVariable = "ResourceManager";
		private const string CultureInfoVariable = "CultureInfo";

		private readonly Stream _resxStream;
		private readonly GeneratorOptions _options;

		public Generator(Stream resxStream, GeneratorOptions options)
		{
			_resxStream = resxStream;
			_options = options;
		}

		private void Dispose(bool disposing)
		{
			if (disposing)
				_resxStream.Dispose();
		}

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		~Generator() => Dispose(false);

		private NamespaceDeclarationSyntax CreateNamespace() =>
			NamespaceDeclaration(ParseName(_options.CustomToolNamespace ?? _options.LocalNamespace))
				.AddUsings(
					UsingDirective(IdentifierName(SystemGlobalization)),
					UsingDirective(IdentifierName(SystemResources)));

		private ClassDeclarationSyntax CreateClass() => ClassDeclaration(_options.ClassName)
			.AddModifiers(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.StaticKeyword))
			.AddMembers(
				FieldDeclaration(VariableDeclaration(NullableType(IdentifierName(nameof(ResourceManager)))).AddVariables(VariableDeclarator(s_resourceManagerVariable)))
					.AddModifiers(Token(SyntaxKind.PrivateKeyword), Token(SyntaxKind.StaticKeyword)),
				PropertyDeclaration(IdentifierName(nameof(ResourceManager)), ResourceManagerVariable)
					.AddModifiers(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.StaticKeyword))
						.WithExpressionBody(
							ArrowExpressionClause(
								AssignmentExpression(
									SyntaxKind.CoalesceAssignmentExpression,
									IdentifierName(s_resourceManagerVariable),
									ObjectCreationExpression(IdentifierName(nameof(ResourceManager)))
										.AddArgumentListArguments(
											Argument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal($"{_options.LocalNamespace}.{_options.ClassName}"))),
											Argument(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, TypeOfExpression(IdentifierName(_options.ClassName)), IdentifierName(nameof(Type.Assembly))))))))
						.WithSemicolonToken(Token(SyntaxKind.SemicolonToken)),
				PropertyDeclaration(NullableType(IdentifierName(nameof(CultureInfo))), CultureInfoVariable)
					.AddModifiers(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.StaticKeyword))
					.AddAccessorListAccessors(
						AccessorDeclaration(SyntaxKind.GetAccessorDeclaration).WithSemicolonToken(Token(SyntaxKind.SemicolonToken)),
						AccessorDeclaration(SyntaxKind.SetAccessorDeclaration).WithSemicolonToken(Token(SyntaxKind.SemicolonToken))));

		private MemberDeclarationSyntax CreateMember(string name, string value) => PropertyDeclaration(NullableType(IdentifierName("string")), name)
			.AddModifiers(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.StaticKeyword))
			.WithExpressionBody(
				ArrowExpressionClause(
					InvocationExpression(
						MemberAccessExpression(
							SyntaxKind.SimpleMemberAccessExpression,
							IdentifierName(ResourceManagerVariable),
							IdentifierName(nameof(ResourceManager.GetString)))).AddArgumentListArguments(
								Argument(InvocationExpression(IdentifierName("nameof")).AddArgumentListArguments(
									Argument(IdentifierName(name)))),
								Argument(IdentifierName(CultureInfoVariable)))))
			.WithSemicolonToken(Token(SyntaxKind.SemicolonToken))
			.WithLeadingTrivia(ParseLeadingTrivia($@"/// <summary>
/// Looks up a localized string similar to {HttpUtility.HtmlEncode(value.Trim().Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n/// "))}.
/// </summary>
"));

		private CompilationUnitSyntax GetCompilationUnit(IEnumerable<MemberDeclarationSyntax> members) => CompilationUnit()
			.AddMembers(
				CreateNamespace()
					.AddMembers(
						CreateClass()
							.AddMembers(members.ToArray())))
			.WithLeadingTrivia(ParseLeadingTrivia(AutoGeneratedHeader).Add(
				Trivia(NullableDirectiveTrivia(Token(SyntaxKind.EnableKeyword), true))))
			.NormalizeWhitespace();

		public CompilationUnitSyntax Generate() => GetCompilationUnit(XDocument.Load(_resxStream).Root?
			.Descendants()
			.Where(data => data.Name == "data")
			.Select(data => new KeyValuePair<string, string>(data.Attribute("name")!.Value, data.Descendants("value").First().Value))
			.Select(kv => CreateMember(kv.Key, kv.Value)) ?? Enumerable.Empty<MemberDeclarationSyntax>());
	}
}
