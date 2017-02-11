﻿namespace AngleSharp.Core.Tests.Library
{
    using AngleSharp.Html;
    using NUnit.Framework;
    using System;

    [TestFixture]
    public class PrettyMarkupPrinter
    {
        [Test]
        public void ElementsArePrintedWithAdditionalWhitespaceInPrettyFormatter()
        {
            var output = Print("<html><head></head><body><i></i></body></html>");
            var result = @"<html>
	<head>
	</head>
	<body>
		<i></i>
	</body>
</html>";

            Assert.AreEqual(result.Replace(Environment.NewLine, "\n"), output);
        }

        [Test]
        public void ElementsWithAttributesArePrintedLikeNormalElementsInPrettyFormatter()
        {
            var output = Print("<html><head></head><body><script src=\"//ajax.aspnetcdn.com/ajax/bootstrap/3.0.1/bootstrap.min.js\"></script><script src=\"//ajax.aspnetcdn.com/ajax/bootstrap/1.9.1/jquery.min.js\"></script></body></html>");
            var result = @"<html>
	<head>
	</head>
	<body>
		<script src=""//ajax.aspnetcdn.com/ajax/bootstrap/3.0.1/bootstrap.min.js""></script>
		<script src=""//ajax.aspnetcdn.com/ajax/jquery/1.9.1/jquery.min.js""></script>
	</body>
</html>";

            Assert.AreEqual(result.Replace(Environment.NewLine, "\n"), output);
        }
        
        [Test]
        public void SelfClosedElementsArePrintedLikeNormalElementsInPrettyFormatter()
        {
            var output = Print("<html><head><meta name=\"this\" content=\"that\"><meta name=\"this\" content=\"that\"></head><body></body></html>");
            var result = @"<html>
	<head>
		<meta name=""this"" content=""that"">
		<meta name=""this"" content=""that"">
	</head>
	<body></body>
</html>";

            Assert.AreEqual(result.Replace(Environment.NewLine, "\n"), output);
        }

        [Test]
        public void StandardWhitespaceIsMergedIntoSerializationOfPrettyFormatter()
        {
            var output = Print(@"
<html>
	<head>
		<meta name=""this"" content=""that"">
		<meta name=""this"" content=""that"">
	</head>
	<body>
	</body>
</html>
");
            var result = @"<html>
	<head>
        <meta name=""this"" content=""that"">
        <meta name=""this"" content=""that"">
    </head>
    <body> </body>
</html>";

            Assert.AreEqual(result.Replace(Environment.NewLine, "\n"), output);
        }

        private static String Print(String html)
        {
            var document = html.ToHtmlDocument();
            var formatter = new PrettyMarkupFormatter();
            return document.ToHtml(formatter);
        }
    }
}
