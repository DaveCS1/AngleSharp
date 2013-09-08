﻿using System;
using System.IO;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Collections.Generic;
using AngleSharp.DOM;
using AngleSharp.Events;
using AngleSharp.DOM.Css;
using AngleSharp.DOM.Collections;

namespace AngleSharp.Css
{
    /// <summary>
    /// The CSS parser.
    /// See http://dev.w3.org/csswg/css-syntax/#parsing for more details.
    /// </summary>
    //[DebuggerStepThrough]
    public sealed class CssParser : IParser
    {
        #region Members

        Boolean _started;
        Boolean _quirksFlag;
        CssTokenizer _tokenizer;
        CSSStyleSheet _sheet;
        Task _task;
        Stack<CSSRule> _open;
        Boolean _ignore;
        Object _lock;

        #endregion

        #region Events

        /// <summary>
        /// The event will be fired once an error has been detected.
        /// </summary>
        public event ParseErrorEventHandler ErrorOccurred;

        #endregion

        #region ctor

        /// <summary>
        /// Creates a new CSS parser instance with a new stylesheet
        /// based on the given source.
        /// </summary>
        /// <param name="source">The source code as a string.</param>
        public CssParser(String source)
            : this(new CSSStyleSheet(), new SourceManager(source))
        {
        }

        /// <summary>
        /// Creates a new CSS parser instance with an new stylesheet
        /// based on the given stream.
        /// </summary>
        /// <param name="stream">The stream to use as source.</param>
        public CssParser(Stream stream)
            : this(new CSSStyleSheet(), new SourceManager(stream))
        {
        }

        /// <summary>
        /// Creates a new CSS parser instance with the specified stylesheet
        /// based on the given source.
        /// </summary>
        /// <param name="stylesheet">The stylesheet to be constructed.</param>
        /// <param name="source">The source code as a string.</param>
        public CssParser(CSSStyleSheet stylesheet, String source)
            : this(stylesheet, new SourceManager(source))
        {
        }

        /// <summary>
        /// Creates a new CSS parser instance with the specified stylesheet
        /// based on the given stream.
        /// </summary>
        /// <param name="stylesheet">The stylesheet to be constructed.</param>
        /// <param name="stream">The stream to use as source.</param>
        public CssParser(CSSStyleSheet stylesheet, Stream stream)
            : this(stylesheet, new SourceManager(stream))
        {
        }

        /// <summary>
        /// Creates a new CSS parser instance parser with the specified stylesheet
        /// based on the given source manager.
        /// </summary>
        /// <param name="stylesheet">The stylesheet to be constructed.</param>
        /// <param name="source">The source to use.</param>
        internal CssParser(CSSStyleSheet stylesheet, SourceManager source)
        {
            _lock = new Object();
            _ignore = true;
            _tokenizer = new CssTokenizer(source);

            _tokenizer.ErrorOccurred += (s, ev) =>
            {
                if (ErrorOccurred != null)
                    ErrorOccurred(this, ev);
            };

            _started = false;
            _sheet = stylesheet;
            _open = new Stack<CSSRule>();
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets if the parser has been started asynchronously.
        /// </summary>
        public Boolean IsAsync
        {
            get { return _task != null; }
        }

        /// <summary>
        /// Gets the resulting stylesheet of the parsing.
        /// </summary>
        public CSSStyleSheet Result
        {
            get 
            {
                Parse();
                return _sheet; 
            }
        }

        /// <summary>
        /// Gets or sets if the quirks-mode is activated.
        /// </summary>
        public Boolean IsQuirksMode
        {
            get { return _quirksFlag; }
            set { _quirksFlag = value; }
        }

        /// <summary>
        /// Gets the current rule if any.
        /// </summary>
        internal CSSRule CurrentRule
        {
            get { return _open.Count > 0 ? _open.Peek() : null; }
        }

        #endregion

        #region Methods

        /// <summary>
        /// Parses the given source asynchronously and creates the stylesheet.
        /// </summary>
        /// <returns>The task which could be awaited or continued differently.</returns>
        public Task ParseAsync()
        {
            lock (_lock)
            {
                if (!_started)
                {
                    _started = true;
                    _task = Task.Run(() => AppendRules());
                }
                else if (_task == null)
                    throw new InvalidOperationException("The parser has already run synchronously.");

                return _task;
            }
        }

        /// <summary>
        /// Parses the given source code.
        /// </summary>
        public void Parse()
        {
            lock (_lock)
            {
                if (!_started)
                {
                    _started = true;
                    AppendRules();
                }
            }
        }

        #endregion

        #region Stylesheet construction

        /// <summary>
        /// Appends rules from the document's source
        /// to the stylesheet's list of rules.
        /// </summary>
        void AppendRules()
        {
            AppendRules(_tokenizer.Iterator, _sheet.CssRules.List);
        }

        /// <summary>
        /// Appends rules from the given source to the list of rules.
        /// </summary>
        /// <param name="source">The token iterator (source).</param>
        /// <param name="rules">The list of rules to append to.</param>
        void AppendRules(IEnumerator<CssToken> source, List<CSSRule> rules)
        {
            while (source.MoveNext())
            {
                switch (source.Current.Type)
                {
                    case CssTokenType.Cdc:
                    case CssTokenType.Cdo:
                    case CssTokenType.Whitespace:
                        break;

                    case CssTokenType.AtKeyword:
                        rules.Add(CreateAtRule(source));
                        break;

                    default:
                        rules.Add(CreateStyleRule(source));
                        break;
                }
            }
        }

        /// <summary>
        /// Appends declarations from the given source to the list of declarations.
        /// </summary>
        /// <param name="source">The token iterator.</param>
        /// <param name="declarations">The list of declarations to append to.</param>
        void AppendDeclarations(IEnumerator<CssToken> source, List<CSSProperty> declarations)
        {
            while (source.MoveNext())
            {
                switch (source.Current.Type)
                {
                    case CssTokenType.Whitespace:
                    case CssTokenType.Semicolon:
                        break;

                    case CssTokenType.Ident:
                        var tokens = LimitToSemicolon(source);
                        var it = tokens.GetEnumerator();
                        it.MoveNext();
                        var decl = CreateDeclaration(it);

                        if (decl != null)
                            declarations.Add(decl);

                        break;

                    default:
                        RaiseErrorOccurred(ErrorCode.InvalidCharacter);
                        SkipToNextSemicolon(source);
                        break;
                }
            }
        }

        /// <summary>
        /// Appends media labels from the given source to the medialist.
        /// </summary>
        /// <param name="source">The token iterator.</param>
        /// <param name="media">The medialist to append to.</param>
        /// <param name="endToken">The optional token type to finish appending to the list.</param>
        void AppendMediaList(IEnumerator<CssToken> source, MediaList media, CssTokenType endToken = CssTokenType.Semicolon)
        {
            do
            {
                if (source.Current.Type == CssTokenType.Whitespace)
                    continue;
                else if (source.Current.Type == endToken)
                    break;

                var buffer = Pool.NewStringBuilder();

                do
                {
                    if (source.Current.Type == CssTokenType.Comma || source.Current.Type == endToken)
                        break;
                    else if (source.Current.Type == CssTokenType.Whitespace)
                        buffer.Append(' ');
                    else
                        buffer.Append(source.Current.ToValue());
                }
                while (source.MoveNext());

                media.AppendMedium(buffer.ToPool());

                if (source.Current.Type == endToken)
                    break;
            }
            while (source.MoveNext());
        }

        /// <summary>
        /// Creates a list of CSSValue values from the given source.
        /// </summary>
        /// <param name="source">The token source.</param>
        /// <returns>The list of CSSValueList instances.</returns>
        List<CSSValue> CreateMultipleValues(IEnumerator<CssToken> source)
        {
            var values = new List<CSSValue>();

            do
            {
                var list = CreateValueList(source);

                if (list.Length == 1)
                    values.Add(list[0]);
                else if (list.Length != 0)
                    values.Add(list);
                else
                    break;
            }
            while (source.Current.Type == CssTokenType.Comma);

            return values;
        }

        /// <summary>
        /// Creates a CSSValueList from the given source.
        /// </summary>
        /// <param name="source">The token source.</param>
        /// <returns>The CSSValueList instance.</returns>
        CSSValueList CreateValueList(IEnumerator<CssToken> source)
        {
            var list = new List<CSSValue>();
            
            if(SkipToNextNonWhitespace(source))
            {
                do
                {
                    if (source.Current.Type == CssTokenType.Comma || source.Current.Type == CssTokenType.Semicolon)
                        break;

                    var value = CreateValue(source);

                    if (value == null)
                        SkipToNextNonWhitespace(source);
                    else
                        list.Add(value);
                }
                while (source.Current.Type == CssTokenType.Whitespace && SkipToNextNonWhitespace(source));
            }

            return new CSSValueList(list, false);
        }

        /// <summary>
        /// Creates a single value from the given source.
        /// </summary>
        /// <param name="source">The token iterator.</param>
        /// <returns>The value or NULL.</returns>
        CSSValue CreateValue(IEnumerator<CssToken> source)
        {
            CSSValue value = null;

            switch (source.Current.Type)
            {
                case CssTokenType.String:// 'i am a string'
                    value = new CSSPrimitiveValue(CssUnit.String, ((CssStringToken)source.Current).Data);
                    source.MoveNext();
                    break;
                case CssTokenType.Url:// url('this is a valid URL')
                    value = new CSSPrimitiveValue(CssUnit.Uri, ((CssStringToken)source.Current).Data);
                    source.MoveNext();
                    break;
                case CssTokenType.Ident: // ident
                    value = new CSSPrimitiveValue(CssUnit.Ident, ((CssKeywordToken)source.Current).Data);
                    source.MoveNext();
                    break;
                case CssTokenType.Percentage: // 5%
                    value = new CSSPrimitiveValue(CssUnit.Percentage, ((CssUnitToken)source.Current).Data);
                    source.MoveNext();
                    break;
                case CssTokenType.Dimension: // 3px
                    value = new CSSPrimitiveValue(((CssUnitToken)source.Current).Unit, ((CssUnitToken)source.Current).Data);
                    source.MoveNext();

                    if (source.Current.Type == CssTokenType.Delim && ((CssDelimToken)source.Current).Data == Specification.SOLIDUS)
                    {
                        source.MoveNext();
                        value = new CSSPrimitiveValue(CssUnit.Unknown, value.ToCss() + "/" + source.Current.ToValue());
                        source.MoveNext();
                    }

                    break;
                case CssTokenType.Number: // 173
                    value = new CSSPrimitiveValue(CssUnit.Number, ((CssNumberToken)source.Current).Data);
                    source.MoveNext();
                    break;
                case CssTokenType.Hash: // #string
                {
                    CSSColor color;

                    if (CSSColor.TryFromHex(((CssKeywordToken)source.Current).Data, out color))
                        value = new CSSPrimitiveValue(color);

                    source.MoveNext();
                    break;
                }
                case CssTokenType.Delim: // e.g. #0F3, #012345, ...
                {
                    CSSColor color;

                    if (((CssDelimToken)source.Current).Data == Specification.NUM)
                    {
                        String hash = String.Empty;

                        while (source.MoveNext())
                        {
                            var stop = false;

                            switch (source.Current.Type)
                            {
                                case CssTokenType.Number:
                                case CssTokenType.Dimension:
                                case CssTokenType.Ident:
                                    var rest = source.Current.ToValue();

                                    if (hash.Length + rest.Length <= 6)
                                        hash += rest;
                                    else
                                        stop = true;

                                    break;

                                default:
                                    stop = true;
                                    break;
                            }

                            if (stop || hash.Length == 6)
                                break;
                        }

                        if (CSSColor.TryFromHex(hash, out color))
                            value = new CSSPrimitiveValue(color);
                    }

                    source.MoveNext();
                    break;
                }
                case CssTokenType.Function: // rgba(255, 255, 20, 0.5)
                {
                    var name = ((CssKeywordToken)source.Current).Data;
                    var args = new List<CSSValue>();

                    if (SkipToNextNonWhitespace(source) && source.Current.Type != CssTokenType.RoundBracketClose)
                    {
                        args.Add(CreateValue(source));
                        SkipWhitespaces(source);

                        while (source.Current.Type == CssTokenType.Comma)
                        {
                            SkipToNextNonWhitespace(source);
                            args.Add(CreateValue(source));
                            SkipWhitespaces(source);
                        }

                        if (source.Current.Type != CssTokenType.RoundBracketClose)
                            RaiseErrorOccurred(ErrorCode.InputUnexpected);
                    }

                    value = CSSFunction.Create(name, args);
                    source.MoveNext();
                    break;
                }
                default:
                    source.MoveNext();
                    break;
            }

            return value;
        }

        /// <summary>
        /// Creates a new style rule from the given source.
        /// </summary>
        /// <param name="source">The token iterator.</param>
        /// <returns>The style rule.</returns>
        CSSStyleRule CreateStyleRule(IEnumerator<CssToken> source)
        {
            var style = new CSSStyleRule();
            var ctor = new CssSelectorConstructor();
            ctor.IgnoreErrors = _ignore;
            style.ParentStyleSheet = _sheet;
            style.ParentRule = CurrentRule;
            _open.Push(style);

            do
            {
                if (source.Current.Type == CssTokenType.CurlyBracketOpen)
                {
                    if (SkipToNextNonWhitespace(source))
                    {
                        var tokens = LimitToCurrentBlock(source);
                        AppendDeclarations(tokens.GetEnumerator(), style.Style.List);
                    }

                    break;
                }

                ctor.PickSelector(source);
            }
            while (source.MoveNext());

            style.Selector = ctor.Result;
            _open.Pop();
            return style;
        }

        /// <summary>
        /// Creates a @-rule from the given source.
        /// </summary>
        /// <param name="source">The token iterator.</param>
        /// <returns>The @-rule.</returns>
        CSSRule CreateAtRule(IEnumerator<CssToken> source)
        {
            var name = ((CssKeywordToken)source.Current).Data;
            SkipToNextNonWhitespace(source);

            switch (name)
            {
                case RuleNames.MEDIA: return CreateMediaRule(source);
                case RuleNames.PAGE: return CreatePageRule(source);
                case RuleNames.IMPORT: return CreateImportRule(source);
                case RuleNames.FONT_FACE: return CreateFontFaceRule(source);
                case RuleNames.CHARSET: return CreateCharsetRule(source);
                case RuleNames.NAMESPACE: return CreateNamespaceRule(source);
                case RuleNames.SUPPORTS: return CreateSupportsRule(source);
                case RuleNames.KEYFRAMES: return CreateKeyframesRule(source);
                case RuleNames.DOCUMENT: return CreateDocumentRule(source);
                default: return CreateUnknownRule(name, source);
            }
        }

        /// <summary>
        /// Creates a new property from the given source.
        /// </summary>
        /// <param name="source">The token iterator starting at the name of the property.</param>
        /// <returns>The new property.</returns>
        CSSProperty CreateDeclaration(IEnumerator<CssToken> source)
        {
            String name = ((CssKeywordToken)source.Current).Data;
            CSSProperty property = null;
            CSSValue value = CSSValue.Inherit;
            Boolean hasValue = SkipToNextNonWhitespace(source) && source.Current.Type == CssTokenType.Colon;

            if (hasValue)
            {
                var list = CreateMultipleValues(source);
                value = list.Count == 1 ? list[0] : new CSSValueList(list, true);
            }

            switch (name)
            {
                //case "azimuth":
                //case "animation":
                //case "animation-delay":
                //case "animation-direction":
                //case "animation-duration":
                //case "animation-fill-mode":
                //case "animation-iteration-count":
                //case "animation-name":
                //case "animation-play-state":
                //case "animation-timing-function":
                //case "background-attachment":
                //case "background-color":
                //case "background-clip":
                //case "background-origin":
                //case "background-size":
                //case "background-image":
                //case "background-position":
                //case "background-repeat":
                //case "background":
                //case "border-color":
                //case "border-spacing":
                //case "border-collapse":
                //case "border-style":
                //case "border-radius":
                //case "box-shadow":
                //case "box-decoration-break":
                //case "break-after":
                //case "break-before":
                //case "break-inside":
                //case "backface-visibility":
                //case "border-top-left-radius":
                //case "border-top-right-radius":
                //case "border-bottom-left-radius":
                //case "border-bottom-right-radius":
                //case "border-image":
                //case "border-image-outset":
                //case "border-image-repeat":
                //case "border-image-source":
                //case "border-image-slice":
                //case "border-image-width":
                //case "border-top":
                //case "border-right":
                //case "border-bottom":
                //case "border-left":
                //case "border-top-color":
                //case "border-left-color":
                //case "border-right-color":
                //case "border-bottom-color":
                //case "border-top-style":
                //case "border-left-style":
                //case "border-right-style":
                //case "border-bottom-style":
                //case "border-top-width":
                //case "border-left-width":
                //case "border-right-width":
                //case "border-bottom-width":
                //case "border-width":
                //case "border":
                //case "bottom":
                //case "columns":
                //case "column-count":
                //case "column-fill":
                //case "column-gap":
                //case "column-rule-color":
                //case "column-rule-style":
                //case "column-rule-width":
                //case "column-span":
                //case "column-width":	
                //case "caption-side":
                //case "clear":
                //case "clip":
                //case "color":
                //case "content":
                //case "counter-increment":
                //case "counter-reset":
                //case "cue-after":
                //case "cue-before":
                //case "cue":
                //case "cursor":
                //case "direction":
                //case "display":
                //case "elevation":
                //case "empty-cells":
                //case "float":
                //case "font-family":
                //case "font-size":
                //case "font-style":
                //case "font-variant":
                //case "font-weight":
                //case "font":
                //case "height":
                //case "left":
                //case "letter-spacing":
                //case "line-height":
                //case "list-style-image":
                //case "list-style-position":
                //case "list-style-type":
                //case "list-style":
                //case "marquee-direction":
                //case "marquee-play-count":
                //case "marquee-speed":
                //case "marquee-style":
                //case "margin-right":
                //case "margin-left":
                //case "margin-top":
                //case "margin-bottom":
                //case "margin":
                //case "max-height":
                //case "max-width":
                //case "min-height":
                //case "min-width":
                //case "opacity":
                //case "orphans":
                //case "outline-color":
                //case "outline-style":
                //case "outline-width":
                //case "outline":
                //case "overflow":
                //case "padding-top":
                //case "padding-right":
                //case "padding-left":
                //case "padding-bottom":
                //case "padding":
                //case "page-break-after":
                //case "page-break-before":
                //case "page-break-inside":
                //case "pause-after":
                //case "pause-before":
                //case "pause":
                //case "perspective":
                //case "perspective-origin":
                //case "pitch-range":
                //case "pitch":
                //case "play-during":
                //case "position":
                //case "quotes":
                //case "richness":
                //case "right":
                //case "speak-header":
                //case "speak-numeral":
                //case "speak-punctuation":
                //case "speak":
                //case "speech-rate":
                //case "stress":
                //case "table-layout":
                //case "text-align":
                //case "text-decoration":
                //case "text-indent":
                //case "text-transform":
                //case "transform":
                //case "transform-origin":
                //case "transform-style":
                //case "transition":
                //case "transition-delay":
                //case "transition-duration":
                //case "transition-timing-function":
                //case "transition-property":
                //case "top":
                //case "unicode-bidi":
                //case "vertical-align":
                //case "visibility":
                //case "voice-family":
                //case "volume":
                //case "white-space":
                //case "widows":
                //case "width":
                //case "word-spacing":
                //case "z-index":
                default:
                    property = new CSSProperty(name);
                    property.Value = value;
                    break;
            }

            if (hasValue)
            {
                while (source.Current.Type == CssTokenType.Delim && ((CssDelimToken)source.Current).Data == Specification.EM && SkipToNextNonWhitespace(source))
                { }
                
                property.Important = source.Current.Type == CssTokenType.Ident && ((CssKeywordToken)source.Current).Data.Equals("important", StringComparison.OrdinalIgnoreCase);
            }

            SkipBehindNextSemicolon(source);
            return property;
        }

        #endregion

        #region Rule creation

        /// <summary>
        /// Creates a new unknown @-rule from the given source.
        /// </summary>
        /// <param name="name">The name of the @-rule.</param>
        /// <param name="source">The token iterator.</param>
        /// <returns>The unknown @-rule.</returns>
        CSSUnknownRule CreateUnknownRule(String name, IEnumerator<CssToken> source)
        {
            var rule = new CSSUnknownRule();
            var endCurly = 0;
            rule.ParentStyleSheet = _sheet;
            rule.ParentRule = CurrentRule;
            _open.Push(rule);
            var buffer = Pool.NewStringBuilder().Append(name).Append(Specification.SPACE);

            do
            {
                if (source.Current.Type == CssTokenType.Semicolon && endCurly == 0)
                {
                    source.MoveNext();
                    break;
                }

                buffer.Append(source.Current.ToString());

                if (source.Current.Type == CssTokenType.CurlyBracketOpen)
                    endCurly++;
                else if (source.Current.Type == CssTokenType.CurlyBracketClose && --endCurly == 0)
                    break;
            }
            while (source.MoveNext());

            rule.SetText(buffer.ToPool());
            _open.Pop();
            return rule;
        }

        /// <summary>
        /// Creates a new @document-rule from the given source.
        /// </summary>
        /// <param name="source">The token iterator.</param>
        /// <returns>The @document-rule.</returns>
        CSSDocumentRule CreateDocumentRule(IEnumerator<CssToken> source)
        {
            var comma = false;
            var document = new CSSDocumentRule();
            document.ParentStyleSheet = _sheet;
            document.ParentRule = CurrentRule;
            _open.Push(document);

            do
            {
                var a = source.Current;

                if (a.Type == CssTokenType.Whitespace && SkipToNextNonWhitespace(source))
                    a = source.Current;

                if (a.Type == CssTokenType.CurlyBracketOpen)
                    break;

                if (comma)
                {
                    if (a.Type != CssTokenType.Comma)
                    {
                        RaiseErrorOccurred(ErrorCode.InputUnexpected);

                        if (a.Type == CssTokenType.Semicolon)
                            break;
                    }

                    comma = false;    
                    continue;
                }

                switch (a.Type)
                {
                    case CssTokenType.Url:
                    {
                        var url = (CssStringToken)a;
                        document.Conditions.Add(Tuple.Create(CSSDocumentRule.DocumentFunction.Url, url.Data));
                        break;
                    }
                    case CssTokenType.UrlPrefix:
                    {
                        var url = (CssStringToken)a;
                        document.Conditions.Add(Tuple.Create(CSSDocumentRule.DocumentFunction.UrlPrefix, url.Data));
                        break;
                    }
                    case CssTokenType.Domain:
                    {
                        var url = (CssStringToken)a;
                        document.Conditions.Add(Tuple.Create(CSSDocumentRule.DocumentFunction.Domain, url.Data));
                        break;
                    }
                    case CssTokenType.Function:
                    {
                        var function = (CssKeywordToken)a;

                        if (String.Compare(function.Data, FunctionNames.REGEXP, StringComparison.OrdinalIgnoreCase) == 0 && source.MoveNext() && source.Current.Type == CssTokenType.String)
                        {
                            var content = (CssStringToken)source.Current;
                            document.Conditions.Add(Tuple.Create(CSSDocumentRule.DocumentFunction.RegExp, content.Data));
                            SkipToNextNonWhitespace(source);
                            break;
                        }

                        RaiseErrorOccurred(ErrorCode.InputUnexpected);
                        break;
                    }
                    default:
                        RaiseErrorOccurred(ErrorCode.InputUnexpected);
                        break;
                }

                comma = true;
            }
            while (source.MoveNext());

            if (SkipToNextNonWhitespace(source))
            {
                var tokens = LimitToCurrentBlock(source);
                AppendRules(tokens.GetEnumerator(), document.CssRules.List);
            }

            _open.Pop();
            return document;
        }

        /// <summary>
        /// Creates a new @keyframes-rule from the given source.
        /// </summary>
        /// <param name="source">The token iterator.</param>
        /// <returns>The @keyframes-rule.</returns>
        CSSKeyframesRule CreateKeyframesRule(IEnumerator<CssToken> source)
        {
            var keyframes = new CSSKeyframesRule();
            keyframes.ParentStyleSheet = _sheet;
            keyframes.ParentRule = CurrentRule;
            _open.Push(keyframes);

            if (source.Current.Type == CssTokenType.Ident)
            {
                keyframes.Name = ((CssKeywordToken)source.Current).Data;
                SkipToNextNonWhitespace(source);

                if (source.Current.Type == CssTokenType.CurlyBracketOpen)
                {
                    SkipToNextNonWhitespace(source);
                    var tokens = LimitToCurrentBlock(source).GetEnumerator();

                    while (SkipToNextNonWhitespace(tokens))
                        keyframes.CssRules.List.Add(CreateKeyframeRule(tokens));
                }
            }

            _open.Pop();
            return keyframes;
        }

        /// <summary>
        /// Creates a new keyframe-rule from the given source.
        /// </summary>
        /// <param name="source">The token iterator.</param>
        /// <returns>The keyframe-rule.</returns>
        CSSKeyframeRule CreateKeyframeRule(IEnumerator<CssToken> source)
        {
            var keyframe = new CSSKeyframeRule();
            keyframe.ParentStyleSheet = _sheet;
            keyframe.ParentRule = CurrentRule;
            _open.Push(keyframe);
            var buffer = Pool.NewStringBuilder();

            do
            {
                if (source.Current.Type == CssTokenType.CurlyBracketOpen)
                {
                    if (SkipToNextNonWhitespace(source))
                    {
                        var tokens = LimitToCurrentBlock(source);
                        AppendDeclarations(tokens.GetEnumerator(), keyframe.Style.List);
                    }

                    break;
                }

                buffer.Append(source.Current.ToString());
            }
            while (source.MoveNext());

            keyframe.KeyText = buffer.ToPool();
            _open.Pop();
            return keyframe;
        }

        /// <summary>
        /// Creates a new @supports-rule from the given source.
        /// </summary>
        /// <param name="source">The token iterator.</param>
        /// <returns>The @supports-rule.</returns>
        CSSSupportsRule CreateSupportsRule(IEnumerator<CssToken> source)
        {
            var supports = new CSSSupportsRule();
            supports.ParentStyleSheet = _sheet;
            supports.ParentRule = CurrentRule;
            _open.Push(supports);
            var buffer = Pool.NewStringBuilder();

            do
            {
                if (source.Current.Type == CssTokenType.CurlyBracketOpen)
                {
                    if (SkipToNextNonWhitespace(source))
                    {
                        var tokens = LimitToCurrentBlock(source);
                        AppendRules(tokens.GetEnumerator(), supports.CssRules.List);
                    }

                    break;
                }

                buffer.Append(source.Current.ToString());
            }
            while (source.MoveNext());

            supports.ConditionText = buffer.ToPool();
            _open.Pop();
            return supports;
        }

        /// <summary>
        /// Creates a new @namespace-rule from the given source.
        /// </summary>
        /// <param name="source">The token iterator.</param>
        /// <returns>The @namespace-rule.</returns>
        CSSNamespaceRule CreateNamespaceRule(IEnumerator<CssToken> source)
        {
            var ns = new CSSNamespaceRule();
            ns.ParentStyleSheet = _sheet;

            if (source.Current.Type == CssTokenType.Ident)
            {
                ns.Prefix = source.Current.ToValue();
                SkipToNextNonWhitespace(source);
                
                if (source.Current.Type == CssTokenType.String)
                    ns.NamespaceURI = source.Current.ToValue();
            }

            SkipToNextSemicolon(source);
            return ns;
        }

        /// <summary>
        /// Creates a new @charset-rule from the given source.
        /// </summary>
        /// <param name="source">The token iterator.</param>
        /// <returns>The @charset-rule.</returns>
        CSSCharsetRule CreateCharsetRule(IEnumerator<CssToken> source)
        {
            var charset = new CSSCharsetRule();
            charset.ParentStyleSheet = _sheet;

            if (source.Current.Type == CssTokenType.String)
                charset.Encoding = ((CssStringToken)source.Current).Data;

            SkipToNextSemicolon(source);
            return charset;
        }

        /// <summary>
        /// Creates a new @font-face-rule from the given source.
        /// </summary>
        /// <param name="source">The token iterator.</param>
        /// <returns>The @font-face-rule.</returns>
        CSSFontFaceRule CreateFontFaceRule(IEnumerator<CssToken> source)
        {
            var fontface = new CSSFontFaceRule();
            fontface.ParentStyleSheet = _sheet;
            fontface.ParentRule = CurrentRule;
            _open.Push(fontface);

            if(source.Current.Type == CssTokenType.CurlyBracketOpen)
            {
                if (SkipToNextNonWhitespace(source))
                {
                    var tokens = LimitToCurrentBlock(source);
                    AppendDeclarations(tokens.GetEnumerator(), fontface.CssRules.List);
                }
            }

            _open.Pop();
            return fontface;
        }

        /// <summary>
        /// Creates a new @import-rule from the given source.
        /// </summary>
        /// <param name="source">The token iterator.</param>
        /// <returns>The @import-rule.</returns>
        CSSImportRule CreateImportRule(IEnumerator<CssToken> source)
        {
            var import = new CSSImportRule();
            import.ParentStyleSheet = _sheet;
            import.ParentRule = CurrentRule;
            _open.Push(import);

            switch (source.Current.Type)
            {
                case CssTokenType.Semicolon:
                    source.MoveNext();
                    break;

                case CssTokenType.String:
                case CssTokenType.Url:
                    import.Href = ((CssStringToken)source.Current).Data;
                    AppendMediaList(source, import.Media, CssTokenType.Semicolon);
                    //TODO
                    //import.StyleSheet = DocumentBuilder.Css(new Uri(import.Href));
                    break;

                default:
                    SkipToNextSemicolon(source);
                    break;
            }

            _open.Pop();
            return import;
        }

        /// <summary>
        /// Creates a new @page-rule from the given source.
        /// </summary>
        /// <param name="source">The token iterator.</param>
        /// <returns>The @page-rule.</returns>
        CSSPageRule CreatePageRule(IEnumerator<CssToken> source)
        {
            var page = new CSSPageRule();
            page.ParentStyleSheet = _sheet;
            page.ParentRule = CurrentRule;
            _open.Push(page);
            var ctor = new CssSelectorConstructor();
            ctor.IgnoreErrors = _ignore;

            do
            {
                if (source.Current.Type == CssTokenType.CurlyBracketOpen)
                {
                    if (SkipToNextNonWhitespace(source))
                    {
                        var tokens = LimitToCurrentBlock(source);
                        AppendDeclarations(tokens.GetEnumerator(), page.Style.List);
                        break;
                    }
                }

                ctor.PickSelector(source);
            }
            while (source.MoveNext());

            page.Selector = ctor.Result;
            _open.Pop();
            return page;
        }

        /// <summary>
        /// Creates a new @media-rule from the given source.
        /// </summary>
        /// <param name="source">The token iterator.</param>
        /// <returns>The @media-rule.</returns>
        CSSMediaRule CreateMediaRule(IEnumerator<CssToken> source)
        {
            var media = new CSSMediaRule();
            media.ParentStyleSheet = _sheet;
            media.ParentRule = CurrentRule;
            _open.Push(media);
            AppendMediaList(source, media.Media, CssTokenType.CurlyBracketOpen);

            if (source.Current.Type == CssTokenType.CurlyBracketOpen)
            {
                if (SkipToNextNonWhitespace(source))
                {
                    var tokens = LimitToCurrentBlock(source);
                    AppendRules(tokens.GetEnumerator(), media.CssRules.List);
                }
            }

            _open.Pop();
            return media;
        }

        #endregion

        #region Value creation

        //TODO

        #endregion

        #region Helpers

        /// <summary>
        /// Moves from the current position to the next position that is not a whitespace
        /// token.
        /// </summary>
        /// <param name="source">The iterator to walk through.</param>
        /// <returns>True if a non-whitespace could be reached, otherwise false (EOF).</returns>
        static Boolean SkipToNextNonWhitespace(IEnumerator<CssToken> source)
        {
            while (source.MoveNext())
                if (source.Current.Type != CssTokenType.Whitespace)
                    return true;

            return false;
        }

        /// <summary>
        /// Skips all whitespaces beginning at the current position.
        /// </summary>
        /// <param name="source">The iterator to walk through.</param>
        static void SkipWhitespaces(IEnumerator<CssToken> source)
        {
            while (source.Current.Type == CssTokenType.Whitespace)
                source.MoveNext();
        }

        /// <summary>
        /// Moves from the current position to the next position that is a semicolon token.
        /// </summary>
        /// <param name="source">The iterator to walk through.</param>
        /// <returns>True if a semicolon could be reached, otherwise false (EOF).</returns>
        static Boolean SkipToNextSemicolon(IEnumerator<CssToken> source)
        {
            do
            {
                if (source.Current.Type == CssTokenType.Semicolon)
                    return true;
            }
            while (source.MoveNext());

            return false;
        }

        /// <summary>
        /// Moves from the current position to the next position that is following a
        /// semicolon token.
        /// </summary>
        /// <param name="source">The iterator to walk through.</param>
        /// <returns>True if a semicolon could be passed, otherwise false (EOF).</returns>
        static Boolean SkipBehindNextSemicolon(IEnumerator<CssToken> source)
        {
            do
            {
                if (source.Current.Type == CssTokenType.Semicolon)
                {
                    source.MoveNext();
                    return true;
                }
            }
            while (source.MoveNext());

            return false;
        }

        /// <summary>
        /// Limits the given iterator to the next semicolon.
        /// </summary>
        /// <param name="source">The iterator to consider.</param>
        /// <returns>An iterator within the specified tokens.</returns>
        static IEnumerable<CssToken> LimitToSemicolon(IEnumerator<CssToken> source)
        {
            do
            {
                if (source.Current.Type == CssTokenType.Semicolon)
                    yield break;

                yield return source.Current;
            }
            while (source.MoveNext());
        }

        /// <summary>
        /// Limits the given iterator to the current block (assuming a curly bracket is open).
        /// </summary>
        /// <param name="source">The iterator to consider.</param>
        /// <returns>An iterator within the specified tokens.</returns>
        static IEnumerable<CssToken> LimitToCurrentBlock(IEnumerator<CssToken> source)
        {
            int open = 1;

            do
            {
                if (source.Current.Type == CssTokenType.CurlyBracketOpen)
                    open++;
                else if (source.Current.Type == CssTokenType.CurlyBracketClose && --open == 0)
                    yield break;

                yield return source.Current;
            }
            while (source.MoveNext());
        }

        #endregion

        #region Static methods

        /// <summary>
        /// Takes a string and transforms it into a selector object.
        /// </summary>
        /// <param name="selector">The string to parse.</param>
        /// <param name="quirksMode">Optional: The status of the quirks mode flag (usually not set).</param>
        /// <returns>The Selector object.</returns>
        public static Selector ParseSelector(String selector, Boolean quirksMode = false)
        {
            var parser = new CssParser(selector);
            parser.IsQuirksMode = quirksMode;
            var tokens = parser._tokenizer.Iterator;
            var ctor = new CssSelectorConstructor();

            while (tokens.MoveNext())
                ctor.PickSelector(tokens);

            return ctor.Result;
        }

        /// <summary>
        /// Takes a string and transforms it into a CSS stylesheet.
        /// </summary>
        /// <param name="stylesheet">The string to parse.</param>
        /// <param name="quirksMode">Optional: The status of the quirks mode flag (usually not set).</param>
        /// <returns>The CSSStyleSheet object.</returns>
        public static CSSStyleSheet ParseStyleSheet(String stylesheet, Boolean quirksMode = false)
        {
            var parser = new CssParser(stylesheet);
            parser.IsQuirksMode = quirksMode;
            return parser.Result;
        }

        /// <summary>
        /// Takes a string and transforms it into a CSS rule.
        /// </summary>
        /// <param name="rule">The string to parse.</param>
        /// <param name="quirksMode">Optional: The status of the quirks mode flag (usually not set).</param>
        /// <returns>The CSSRule object.</returns>
        public static CSSRule ParseRule(String rule, Boolean quirksMode = false)
        {
            var parser = new CssParser(rule);
            parser._ignore = false;
            parser.IsQuirksMode = quirksMode;
            var it = parser._tokenizer.Iterator;

            if (SkipToNextNonWhitespace(it))
            {
                if (it.Current.Type == CssTokenType.Cdo || it.Current.Type == CssTokenType.Cdc)
                    throw new DOMException(ErrorCode.SyntaxError);

                return (it.Current.Type == CssTokenType.AtKeyword) ? parser.CreateAtRule(it) : parser.CreateStyleRule(it);
            }

            return new CSSUnknownRule();
        }

        /// <summary>
        /// Takes a string and appends all rules to the given list of properties.
        /// </summary>
        /// <param name="list">The list of css properties to append to.</param>
        /// <param name="declarations">The string to parse.</param>
        /// <param name="quirksMode">Optional: The status of the quirks mode flag (usually not set).</param>
        public static void AppendDeclarations(List<CSSProperty> list, String declarations, Boolean quirksMode = false)
        {
            var parser = new CssParser(declarations);
            parser.IsQuirksMode = quirksMode;
            parser._ignore = false;
            var it = parser._tokenizer.Iterator;
            parser.AppendDeclarations(it, list);
        }

        /// <summary>
        /// Takes a string and transforms it into CSS declarations.
        /// </summary>
        /// <param name="declarations">The string to parse.</param>
        /// <param name="quirksMode">Optional: The status of the quirks mode flag (usually not set).</param>
        /// <returns>The CSSStyleDeclaration object.</returns>
        public static CSSStyleDeclaration ParseDeclarations(String declarations, Boolean quirksMode = false)
        {
            var decl = new CSSStyleDeclaration();
            AppendDeclarations(decl.List, declarations, quirksMode);
            return decl;
        }

        /// <summary>
        /// Takes a string and transforms it into a CSS declaration (CSS property).
        /// </summary>
        /// <param name="declarations">The string to parse.</param>
        /// <param name="quirksMode">Optional: The status of the quirks mode flag (usually not set).</param>
        /// <returns>The CSSProperty object.</returns>
        public static CSSProperty ParseDeclaration(String declarations, Boolean quirksMode = false)
        {
            var parser = new CssParser(declarations);
            parser.IsQuirksMode = quirksMode;
            parser._ignore = false;
            var it = parser._tokenizer.Iterator;

            if (it.MoveNext())
                return parser.CreateDeclaration(it);

            return null;
        }

        /// <summary>
        /// Takes a string and transforms it into a CSS value.
        /// </summary>
        /// <param name="source">The string to parse.</param>
        /// <param name="quirksMode">Optional: The status of the quirks mode flag (usually not set).</param>
        /// <returns>The CSSValue object.</returns>
        public static CSSValue ParseValue(String source, Boolean quirksMode = false)
        {
            var parser = new CssParser(source);
            parser.IsQuirksMode = quirksMode;
            parser._ignore = false;
            var it = parser._tokenizer.Iterator;
            SkipToNextNonWhitespace(it);
            return parser.CreateValue(it);
        }

        /// <summary>
        /// Takes a string and transforms it into a list of CSS values.
        /// </summary>
        /// <param name="source">The string to parse.</param>
        /// <param name="quirksMode">Optional: The status of the quirks mode flag (usually not set).</param>
        /// <returns>The CSSValueList object.</returns>
        internal static CSSValueList ParseValueList(String source, Boolean quirksMode = false)
        {
            var parser = new CssParser(source);
            parser.IsQuirksMode = quirksMode;
            parser._ignore = false;
            var it = parser._tokenizer.Iterator;
            return parser.CreateValueList(it);
        }

        /// <summary>
        /// Takes a comma separated string and transforms it into a list of CSS values.
        /// </summary>
        /// <param name="source">The string to parse.</param>
        /// <param name="quirksMode">Optional: The status of the quirks mode flag (usually not set).</param>
        /// <returns>The CSSValueList object.</returns>
        internal static List<CSSValue> ParseMultipleValues(String source, Boolean quirksMode = false)
        {
            var parser = new CssParser(source);
            parser.IsQuirksMode = quirksMode;
            parser._ignore = false;
            var it = parser._tokenizer.Iterator;
            return parser.CreateMultipleValues(it);
        }

        /// <summary>
        /// Takes a string and transforms it into a CSS keyframe rule.
        /// </summary>
        /// <param name="rule">The string to parse.</param>
        /// <param name="quirksMode">Optional: The status of the quirks mode flag (usually not set).</param>
        /// <returns>The CSSKeyframeRule object.</returns>
        internal static CSSKeyframeRule ParseKeyframeRule(String rule, Boolean quirksMode = false)
        {
            var parser = new CssParser(rule);
            parser.IsQuirksMode = quirksMode;
            parser._ignore = false;
            var it = parser._tokenizer.Iterator;

            if (SkipToNextNonWhitespace(it))
            {
                if (it.Current.Type == CssTokenType.Cdo || it.Current.Type == CssTokenType.Cdc)
                    throw new DOMException(ErrorCode.SyntaxError);

                return parser.CreateKeyframeRule(it);
            }

            return null;
        }

        #endregion

        #region Event-Helpers

        /// <summary>
        /// Fires an error occurred event.
        /// </summary>
        /// <param name="code">The associated error code.</param>
        void RaiseErrorOccurred(ErrorCode code)
        {
            if (ErrorOccurred != null)
            {
                var pck = new ParseErrorEventArgs((int)code, Errors.GetError(code));
                pck.Line = _tokenizer.Stream.Line;
                pck.Column = _tokenizer.Stream.Column;
                ErrorOccurred(this, pck);
            }
        }

        #endregion
    }
}
