using System.Buffers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace System.Text.Yaml;

/// <summary>
/// Character-level YAML 1.2 scanner. Converts a byte sequence into a stream of tokens.
/// Implements simple-key tracking, indent stack, and flow-level handling per the YAML spec.
/// Modeled after libyaml's scanner architecture.
/// </summary>
internal sealed class Utf8YamlScanner
{
    private readonly string _input;
    private readonly bool _skipComments;

    // Cursor state
    private int _position;
    private int _line;
    private int _column;

    // Token buffer (List for O(1) indexed insert; _tokenHead tracks the dequeue position)
    private readonly List<YamlScannerToken> _tokens = new(32);
    private int _tokenHead;
    private int _tokensParsed;
    private bool _tokenAvailable;

    // Stream state
    private bool _streamStartProduced;
    private bool _streamEndProduced;

    // Indentation tracking
    private int _indent = -1;
    private readonly Stack<int> _indents = new();

    // Flow level
    private int _flowLevel;

    // Simple key tracking
    private readonly Stack<SimpleKeyState> _simpleKeys = new();
    private bool _simpleKeyAllowed;

    // Adjacent value: after a JSON-like key (quoted scalar) in flow context,
    // ':' is always a value indicator (even without trailing whitespace).
    private bool _adjacentValueAllowed;

    // Reusable StringBuilders to reduce allocations in hot scalar-scanning paths
    private readonly StringBuilder _sbValue = new(256);
    private readonly StringBuilder _sbWhitespace = new(64);
    private readonly StringBuilder _sbLeadingBreak = new(16);
    private readonly StringBuilder _sbTrailingBreaks = new(64);

    public Utf8YamlScanner(string input, bool skipComments = true)
    {
        _input = input?.Replace("\r\n", "\n").Replace('\r', '\n') ?? throw new ArgumentNullException(nameof(input));
        _skipComments = skipComments;
    }

    public YamlScannerToken? Current { get; private set; }

    public YamlMark CurrentMark => new(_position, _line, _column);

    /// <summary>
    /// Advance to the next token.
    /// </summary>
    public bool MoveNext()
    {
        _tokenAvailable = false;
        Current = null;

        if (!_tokenAvailable && !_streamEndProduced)
        {
            FetchMoreTokens();
        }

        if (_tokenHead < _tokens.Count)
        {
            Current = _tokens[_tokenHead++];
            _tokenAvailable = false;
            _tokensParsed++;

            // Compact the list periodically to avoid unbounded growth
            if (_tokenHead > 64 && _tokenHead > _tokens.Count / 2)
            {
                _tokens.RemoveRange(0, _tokenHead);
                _tokenHead = 0;
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// Read all tokens into a list (for compatibility with existing parser).
    /// </summary>
    public List<YamlScannerToken> ReadAllTokens()
    {
        var tokens = new List<YamlScannerToken>();
        while (MoveNext())
        {
            tokens.Add(Current!.Value);
        }

        return tokens;
    }

    // -----------------------------------------------------------------------
    // Core fetch loop (libyaml: yaml_parser_fetch_more_tokens)
    // -----------------------------------------------------------------------

    private void FetchMoreTokens()
    {
        while (true)
        {
            if (_tokenHead >= _tokens.Count)
            {
                FetchNextToken();
            }

            StaleSimpleKeys();

            var needMore = false;
            foreach (var simpleKey in _simpleKeys)
            {
                if (simpleKey.IsPossible && simpleKey.TokenNumber == _tokensParsed)
                {
                    needMore = true;
                    break;
                }
            }

            if (!needMore)
            {
                break;
            }

            FetchNextToken();
        }

        _tokenAvailable = true;
    }

    // -----------------------------------------------------------------------
    // Token dispatch (libyaml: yaml_parser_fetch_next_token)
    // -----------------------------------------------------------------------

    private void FetchNextToken()
    {
        if (!_streamStartProduced)
        {
            FetchStreamStart();
            return;
        }

        ScanToNextToken();
        StaleSimpleKeys();
        UnrollIndent(_column);

        if (IsEof())
        {
            FetchStreamEnd();
            return;
        }

        var ch = Peek();

        // Directives
        if (_column == 0 && ch == '%')
        {
            FetchDirective();
            return;
        }

        // Document indicators
        if (_column == 0 && Check('-', 0) && Check('-', 1) && Check('-', 2) && IsWhiteBreakOrEof(3))
        {
            FetchDocumentIndicator(YamlScannerTokenType.DocumentStart);
            return;
        }

        if (_column == 0 && Check('.', 0) && Check('.', 1) && Check('.', 2) && IsWhiteBreakOrEof(3))
        {
            FetchDocumentIndicator(YamlScannerTokenType.DocumentEnd);
            return;
        }

        // Flow collection indicators
        switch (ch)
        {
            case '[':
                FetchFlowCollectionStart(YamlScannerTokenType.FlowSequenceStart);
                return;
            case '{':
                FetchFlowCollectionStart(YamlScannerTokenType.FlowMappingStart);
                return;
            case ']':
                FetchFlowCollectionEnd(YamlScannerTokenType.FlowSequenceEnd);
                return;
            case '}':
                FetchFlowCollectionEnd(YamlScannerTokenType.FlowMappingEnd);
                return;
            case ',':
                FetchFlowEntry();
                return;
        }

        // Block entry
        if (ch == '-' && IsWhiteBreakOrEof(1))
        {
            FetchBlockEntry();
            return;
        }

        // Key indicator: in flow context, only if followed by whitespace/break/EOF
        if (ch == '?' && (_flowLevel > 0 ? IsWhiteBreakOrEof(1) : IsWhiteBreakOrEof(1)))
        {
            FetchKey();
            return;
        }

        // Value indicator: in flow context, only if followed by whitespace/break/EOF/flow-indicator
        // OR if immediately after a quoted scalar (JSON adjacent value)
        if (ch == ':' && (_flowLevel > 0 ? (IsPeekFlowIndicator(1) || _adjacentValueAllowed) : IsWhiteBreakOrEof(1)))
        {
            FetchValue();
            return;
        }

        // Anchor & Alias
        if (ch == '&')
        {
            FetchAnchorOrAlias(isAlias: false);
            return;
        }

        if (ch == '*')
        {
            FetchAnchorOrAlias(isAlias: true);
            return;
        }

        // Tag
        if (ch == '!')
        {
            FetchTag();
            return;
        }

        // Block scalars
        if (ch == '|' && _flowLevel == 0)
        {
            FetchBlockScalar(literal: true);
            return;
        }

        if (ch == '>' && _flowLevel == 0)
        {
            FetchBlockScalar(literal: false);
            return;
        }

        // Quoted scalars
        if (ch == '\'')
        {
            FetchFlowScalar(singleQuoted: true);
            return;
        }

        if (ch == '"')
        {
            FetchFlowScalar(singleQuoted: false);
            return;
        }

        // Plain scalar (default)
        if (IsPlainScalarStart(ch))
        {
            FetchPlainScalar();
            return;
        }

        throw new YamlException($"Unexpected character '{ch}' (U+{(int)ch:X4}) at {CurrentMark}.");
    }

    // -----------------------------------------------------------------------
    // Stream start/end
    // -----------------------------------------------------------------------

    private void FetchStreamStart()
    {
        _simpleKeys.Push(SimpleKeyState.Impossible);
        _simpleKeyAllowed = true;
        _streamStartProduced = true;

        var mark = CurrentMark;

        // Skip BOM
        if (_position < _input.Length && _input[_position] == '\uFEFF')
        {
            Advance();
        }

        _tokens.Add(new YamlScannerToken(YamlScannerTokenType.StreamStart, mark, CurrentMark));
    }

    private void FetchStreamEnd()
    {
        UnrollIndent(-1);
        RemoveSimpleKey();
        _simpleKeyAllowed = false;

        var mark = CurrentMark;
        _streamEndProduced = true;
        _tokens.Add(new YamlScannerToken(YamlScannerTokenType.StreamEnd, mark, mark));
    }

    // -----------------------------------------------------------------------
    // Document indicators
    // -----------------------------------------------------------------------

    private void FetchDocumentIndicator(YamlScannerTokenType type)
    {
        UnrollIndent(-1);
        RemoveSimpleKey();
        _simpleKeyAllowed = false;

        var start = CurrentMark;
        Advance(3);
        _tokens.Add(new YamlScannerToken(type, start, CurrentMark));
    }

    // -----------------------------------------------------------------------
    // Directives
    // -----------------------------------------------------------------------

    private void FetchDirective()
    {
        UnrollIndent(-1);
        RemoveSimpleKey();
        _simpleKeyAllowed = false;

        var start = CurrentMark;
        var line = ScanDirectiveLine();
        _tokens.Add(new YamlScannerToken(YamlScannerTokenType.Tag, start, CurrentMark, line));
    }

    private string ScanDirectiveLine()
    {
        var sb = new StringBuilder();
        while (!IsEof() && !IsBreak())
        {
            sb.Append(Peek());
            Advance();
        }

        if (!IsEof())
        {
            AdvanceLine();
        }

        return sb.ToString();
    }

    // -----------------------------------------------------------------------
    // Flow collections
    // -----------------------------------------------------------------------

    private void FetchFlowCollectionStart(YamlScannerTokenType type)
    {
        SaveSimpleKey();
        IncreaseFlowLevel();
        _simpleKeyAllowed = true;

        var start = CurrentMark;
        Advance();
        _tokens.Add(new YamlScannerToken(type, start, CurrentMark));
    }

    private void FetchFlowCollectionEnd(YamlScannerTokenType type)
    {
        RemoveSimpleKey();
        DecreaseFlowLevel();
        _simpleKeyAllowed = false;

        var start = CurrentMark;
        Advance();
        _tokens.Add(new YamlScannerToken(type, start, CurrentMark));
    }

    private void FetchFlowEntry()
    {
        RemoveSimpleKey();
        _simpleKeyAllowed = true;

        var start = CurrentMark;
        Advance();
        _tokens.Add(new YamlScannerToken(YamlScannerTokenType.FlowEntry, start, CurrentMark));
    }

    // -----------------------------------------------------------------------
    // Block entry (- )
    // -----------------------------------------------------------------------

    private void FetchBlockEntry()
    {
        if (_flowLevel == 0)
        {
            if (!_simpleKeyAllowed)
            {
                throw new YamlException($"Block sequence entries are not allowed in this context at {CurrentMark}.");
            }

            RollIndent(_column, YamlScannerTokenType.BlockSequenceStart, CurrentMark);
        }

        RemoveSimpleKey();
        _simpleKeyAllowed = true;

        var start = CurrentMark;
        Advance();
        _tokens.Add(new YamlScannerToken(YamlScannerTokenType.BlockEntry, start, CurrentMark));
    }

    // -----------------------------------------------------------------------
    // Key (?)
    // -----------------------------------------------------------------------

    private void FetchKey()
    {
        if (_flowLevel == 0)
        {
            if (!_simpleKeyAllowed)
            {
                throw new YamlException($"Mapping keys are not allowed in this context at {CurrentMark}.");
            }

            RollIndent(_column, YamlScannerTokenType.BlockMappingStart, CurrentMark);
        }

        RemoveSimpleKey();
        _simpleKeyAllowed = _flowLevel == 0;

        var start = CurrentMark;
        Advance();
        _tokens.Add(new YamlScannerToken(YamlScannerTokenType.Key, start, CurrentMark));
    }

    // -----------------------------------------------------------------------
    // Value (:)
    // -----------------------------------------------------------------------

    private void FetchValue()
    {
        var simpleKey = _simpleKeys.Peek();
        if (simpleKey.IsPossible)
        {
            // Insert KEY token before the simple key's token
            var keyToken = new YamlScannerToken(YamlScannerTokenType.Key, simpleKey.Mark, simpleKey.Mark);
            InsertToken(simpleKey.TokenNumber - _tokensParsed, keyToken);

            if (_flowLevel == 0)
            {
                RollIndent(simpleKey.Mark.Column, YamlScannerTokenType.BlockMappingStart, simpleKey.Mark, simpleKey.TokenNumber - _tokensParsed);
            }

            // Pop and mark as impossible
            _simpleKeys.Pop();
            _simpleKeys.Push(SimpleKeyState.Impossible);

            _simpleKeyAllowed = false;
        }
        else
        {
            if (_flowLevel == 0)
            {
                if (!_simpleKeyAllowed)
                {
                    throw new YamlException($"Mapping values are not allowed in this context at {CurrentMark}.");
                }

                RollIndent(_column, YamlScannerTokenType.BlockMappingStart, CurrentMark);
            }

            _simpleKeyAllowed = _flowLevel == 0;
        }

        var start = CurrentMark;
        Advance();
        _adjacentValueAllowed = false;
        _tokens.Add(new YamlScannerToken(YamlScannerTokenType.Value, start, CurrentMark));
    }

    // -----------------------------------------------------------------------
    // Anchor & Alias
    // -----------------------------------------------------------------------

    private void FetchAnchorOrAlias(bool isAlias)
    {
        SaveSimpleKey();
        _simpleKeyAllowed = false;

        var start = CurrentMark;
        Advance(); // skip & or *

        var sb = new StringBuilder();
        while (!IsEof() && IsAnchorChar(Peek()))
        {
            sb.Append(Peek());
            Advance();
        }

        if (sb.Length == 0)
        {
            throw new YamlException($"Empty anchor or alias name at {start}.");
        }

        var type = isAlias ? YamlScannerTokenType.Alias : YamlScannerTokenType.Anchor;
        _tokens.Add(new YamlScannerToken(type, start, CurrentMark, sb.ToString()));
    }

    // -----------------------------------------------------------------------
    // Tag
    // -----------------------------------------------------------------------

    private void FetchTag()
    {
        SaveSimpleKey();
        _simpleKeyAllowed = false;

        var start = CurrentMark;
        var tag = ScanTag();
        _tokens.Add(new YamlScannerToken(YamlScannerTokenType.Tag, start, CurrentMark, tag));
    }

    private string ScanTag()
    {
        var sb = new StringBuilder();
        sb.Append(Peek());
        Advance(); // first !

        if (!IsEof() && Peek() == '<')
        {
            // Verbatim tag
            sb.Append(Peek());
            Advance();
            while (!IsEof() && Peek() != '>')
            {
                sb.Append(Peek());
                Advance();
            }

            if (!IsEof())
            {
                sb.Append(Peek());
                Advance();
            }
        }
        else
        {
            // Tag handle or shorthand
            while (!IsEof() && !IsWhiteBreakOrEof(0) && Peek() is not (',' or '[' or ']' or '{' or '}'))
            {
                sb.Append(Peek());
                Advance();
            }
        }

        return sb.ToString();
    }

    // -----------------------------------------------------------------------
    // Block scalars (| and >)
    // -----------------------------------------------------------------------

    private void FetchBlockScalar(bool literal)
    {
        RemoveSimpleKey();
        _simpleKeyAllowed = true;

        var start = CurrentMark;
        var scalar = ScanBlockScalar(literal);
        _tokens.Add(new YamlScannerToken(YamlScannerTokenType.Scalar, start, CurrentMark, scalar,
            literal ? ScalarStyle.Literal : ScalarStyle.Folded));
    }

    private string ScanBlockScalar(bool literal)
    {
        Advance(); // skip | or >

        // Parse header: [indentation indicator] [chomping indicator]
        var chomp = 0; // 0 = clip, 1 = keep, -1 = strip
        var increment = 0;

        while (!IsEof() && !IsBreak())
        {
            var ch = Peek();
            if (ch == '+')
            {
                chomp = 1;
                Advance();
            }
            else if (ch == '-')
            {
                chomp = -1;
                Advance();
            }
            else if (ch is >= '1' and <= '9')
            {
                increment = ch - '0';
                Advance();
            }
            else if (ch == ' ' || ch == '\t')
            {
                Advance();
            }
            else if (ch == '#')
            {
                while (!IsEof() && !IsBreak())
                {
                    Advance();
                }
            }
            else
            {
                break;
            }
        }

        if (!IsEof() && IsBreak())
        {
            AdvanceLine();
        }

        // Determine content indentation
        var indent = _indent + 1;
        if (indent < 0)
        {
            indent = 0;
        }

        int blockIndent;
        if (increment > 0)
        {
            blockIndent = indent + increment - 1;
        }
        else
        {
            blockIndent = 0; // auto-detect
        }

        // Scan block scalar content (reuse pooled StringBuilders)
        _sbValue.Clear();
        _sbLeadingBreak.Clear();
        _sbTrailingBreaks.Clear();
        var content = _sbValue;
        var leadingBreak = _sbLeadingBreak;
        var trailingBreaks = _sbTrailingBreaks;
        var hasLeadingBlanks = false;
        var blockIndentDetected = blockIndent > 0;

        // Eat leading empty lines and detect indent
        while (true)
        {
            // Eat indentation
            while ((!blockIndentDetected || _column < blockIndent) && !IsEof() && Peek() == ' ')
            {
                Advance();
            }

            if (blockIndentDetected && _column > blockIndent)
            {
                // more content (extra indentation)
            }
            else if (!blockIndentDetected && !IsEof() && !IsBreak())
            {
                if (_column >= indent)
                {
                    blockIndent = _column;
                    blockIndentDetected = true;
                }
                else
                {
                    break; // content at lower indent — not part of this block scalar
                }
            }

            if (IsEof() || !IsBreak())
            {
                break;
            }

            trailingBreaks.Append(ReadLine());
        }

        if (!blockIndentDetected)
        {
            blockIndent = indent;
        }

        // Read content lines
        var leadingBlank = false;
        var trailingBlank = false;

        while (!IsEof() && _column >= blockIndent)
        {
            // Check for document indicators (--- or ...) at column 0
            if (_column == 0 && (
                (Check('-', 0) && Check('-', 1) && Check('-', 2) && IsWhiteBreakOrEof(3)) ||
                (Check('.', 0) && Check('.', 1) && Check('.', 2) && IsWhiteBreakOrEof(3))))
            {
                break;
            }

            // Is it a trailing blank?
            trailingBlank = Peek() == ' ' || Peek() == '\t';

            // Process the leading break
            if (!literal && leadingBreak.Length > 0 && leadingBreak[0] == '\n' && !leadingBlank && !trailingBlank)
            {
                if (trailingBreaks.Length == 0)
                {
                    content.Append(' ');
                }

                leadingBreak.Length = 0;
            }
            else
            {
                content.Append(leadingBreak);
                leadingBreak.Length = 0;
            }

            content.Append(trailingBreaks);
            trailingBreaks.Length = 0;

            leadingBlank = Peek() == ' ' || Peek() == '\t';

            // Consume content until line break
            while (!IsEof() && !IsBreak())
            {
                content.Append(Peek());
                Advance();
            }

            // Consume line break
            if (!IsEof())
            {
                leadingBreak.Append(ReadLine());
            }

            // Eat indentation and line breaks
            while (true)
            {
                while ((_column < blockIndent || (blockIndent > 0 && _column == blockIndent && IsBreak())) && !IsEof() && Peek() == ' ')
                {
                    Advance();
                }

                if (!IsEof() && IsBreak())
                {
                    trailingBreaks.Append(ReadLine());
                }
                else
                {
                    break;
                }
            }
        }

        // Chomp
        if (chomp != -1)
        {
            if (leadingBreak.Length > 0)
            {
                content.Append(leadingBreak);
            }
            else if (content.Length > 0)
            {
                // EOF without trailing newline — add the implied final line break
                content.Append('\n');
            }
        }

        if (chomp == 1)
        {
            content.Append(trailingBreaks);
        }

        return content.ToString();
    }

    // -----------------------------------------------------------------------
    // Quoted scalars (' and ")
    // -----------------------------------------------------------------------

    private void FetchFlowScalar(bool singleQuoted)
    {
        SaveSimpleKey();
        _simpleKeyAllowed = false;

        var start = CurrentMark;
        var scalar = ScanFlowScalar(singleQuoted);
        _tokens.Add(new YamlScannerToken(YamlScannerTokenType.Scalar, start, CurrentMark, scalar,
            singleQuoted ? ScalarStyle.SingleQuoted : ScalarStyle.DoubleQuoted));

        // After a quoted scalar in flow context, ':' immediately after is an adjacent value indicator
        if (_flowLevel > 0)
        {
            _adjacentValueAllowed = true;
        }
    }

    private string ScanFlowScalar(bool singleQuoted)
    {
        var quote = Peek();
        Advance(); // opening quote

        _sbValue.Clear();
        _sbWhitespace.Clear();
        _sbLeadingBreak.Clear();
        _sbTrailingBreaks.Clear();
        var value = _sbValue;
        var whitespaces = _sbWhitespace;
        var leadingBreak = _sbLeadingBreak;
        var trailingBreaks = _sbTrailingBreaks;
        var hasLeadingBlanks = false;

        while (true)
        {
            if (IsEof())
            {
                throw new YamlException($"Unterminated quoted scalar at {CurrentMark}.");
            }

            hasLeadingBlanks = false;

            // Consume non-blank characters
            while (!IsEof() && !IsWhiteOrBreak())
            {
                if (singleQuoted && Peek() == '\'' && PeekAt(1) == '\'')
                {
                    value.Append('\'');
                    Advance(2);
                }
                else if (Peek() == (singleQuoted ? '\'' : '"'))
                {
                    break;
                }
                else if (!singleQuoted && Peek() == '\\')
                {
                    Advance(); // skip the backslash
                    if (!IsEof() && IsBreak())
                    {
                        // Escaped line break: consume break and skip leading whitespace on next line
                        AdvanceLine();
                        while (!IsEof() && (Peek() == ' ' || Peek() == '\t'))
                        {
                            Advance();
                        }

                        continue; // nothing added to value
                    }

                    value.Append(ScanEscapeSequence());
                    continue;
                }
                else
                {
                    value.Append(Peek());
                    Advance();
                }
            }

            // Check for closing quote
            if (!IsEof() && Peek() == (singleQuoted ? '\'' : '"'))
            {
                break;
            }

            // Consume whitespace and line breaks
            while (IsWhite() || IsBreak())
            {
                if (IsWhite())
                {
                    if (!hasLeadingBlanks)
                    {
                        whitespaces.Append(Peek());
                        Advance();
                    }
                    else
                    {
                        Advance();
                    }
                }
                else
                {
                    if (!hasLeadingBlanks)
                    {
                        whitespaces.Length = 0;
                        leadingBreak.Append(ReadLine());
                        hasLeadingBlanks = true;
                    }
                    else
                    {
                        trailingBreaks.Append(ReadLine());
                    }
                }
            }

            // Join the whitespace or fold line breaks
            if (hasLeadingBlanks)
            {
                if (leadingBreak.Length > 0 && leadingBreak[0] == '\n')
                {
                    if (trailingBreaks.Length == 0)
                    {
                        value.Append(' ');
                    }
                    else
                    {
                        value.Append(trailingBreaks);
                    }
                }
                else
                {
                    value.Append(leadingBreak);
                    value.Append(trailingBreaks);
                }

                leadingBreak.Length = 0;
                trailingBreaks.Length = 0;
            }
            else
            {
                value.Append(whitespaces);
                whitespaces.Length = 0;
            }
        }

        // Eat closing quote
        Advance();

        return value.ToString();
    }

    private char ScanEscapeSequence()
    {
        if (IsEof())
        {
            throw new YamlException($"Truncated escape sequence at {CurrentMark}.");
        }

        var ch = Peek();
        Advance();

        return ch switch
        {
            '0' => '\0',
            'a' => '\a',
            'b' => '\b',
            't' or '\t' => '\t',
            'n' => '\n',
            'v' => '\v',
            'f' => '\f',
            'r' => '\r',
            'e' => '\x1B',
            ' ' => ' ',
            '"' => '"',
            '/' => '/',
            '\\' => '\\',
            'N' => '\x85',
            '_' => '\xA0',
            'L' => '\u2028',
            'P' => '\u2029',
            'x' => ScanHexEscape(2),
            'u' => ScanHexEscape(4),
            'U' => ScanHexEscape(8),
            '\n' or '\r' => ScanEscapedLineBreak(),
            _ => ch
        };
    }

    private char ScanHexEscape(int length)
    {
        var sb = new StringBuilder(length);
        for (var i = 0; i < length; i++)
        {
            if (IsEof())
            {
                throw new YamlException($"Truncated hex escape at {CurrentMark}.");
            }

            sb.Append(Peek());
            Advance();
        }

        try
        {
            var codepoint = Convert.ToInt32(sb.ToString(), 16);
            return codepoint <= 0xFFFF ? (char)codepoint : throw new YamlException($"Codepoint U+{codepoint:X} requires surrogates at {CurrentMark}.");
        }
        catch (FormatException)
        {
            throw new YamlException($"Invalid hex escape at {CurrentMark}.");
        }
    }

    private char ScanEscapedLineBreak()
    {
        // Back up since we consumed the line break char
        // Actually the ReadLine already advanced; skip leading whitespace
        if (_position > 0 && _input[_position - 1] == '\r' && !IsEof() && Peek() == '\n')
        {
            Advance();
        }

        while (!IsEof() && (Peek() == ' ' || Peek() == '\t'))
        {
            Advance();
        }

        return '\0'; // escaped line break produces nothing
    }

    // -----------------------------------------------------------------------
    // Plain scalars
    // -----------------------------------------------------------------------

    private void FetchPlainScalar()
    {
        SaveSimpleKey();
        _simpleKeyAllowed = false;

        var start = CurrentMark;
        var scalar = ScanPlainScalar();
        _simpleKeyAllowed = true;
        _tokens.Add(new YamlScannerToken(YamlScannerTokenType.Scalar, start, CurrentMark, scalar, ScalarStyle.Plain));
    }

    private string ScanPlainScalar()
    {
        _sbValue.Clear();
        _sbWhitespace.Clear();
        _sbLeadingBreak.Clear();
        _sbTrailingBreaks.Clear();
        var value = _sbValue;
        var whitespaces = _sbWhitespace;
        var leadingBreak = _sbLeadingBreak;
        var trailingBreaks = _sbTrailingBreaks;
        var hasLeadingBlanks = false;
        var startIndent = _indent + 1;

        while (true)
        {
            // Consume non-blank characters
            while (!IsEof())
            {
                var ch = Peek();

                // Document indicators at the start of a line
                if (_column == 0 && (
                    (Check('-', 0) && Check('-', 1) && Check('-', 2) && IsWhiteBreakOrEof(3)) ||
                    (Check('.', 0) && Check('.', 1) && Check('.', 2) && IsWhiteBreakOrEof(3))))
                {
                    break;
                }

                // Comment?
                if (ch == '#' && (_position == 0 || IsWhiteAt(-1) || PeekAt(-1) is '\n' or '\r'))
                {
                    break;
                }

                // Value indicator in block context?
                if (ch == ':' && IsWhiteBreakOrEof(1) && _flowLevel == 0)
                {
                    break;
                }

                // Value indicator in flow context?
                if (_flowLevel > 0 && ch == ':' && IsPeekFlowIndicator(1))
                {
                    break;
                }

                // Flow indicators?
                if (_flowLevel > 0 && ch is ',' or '[' or ']' or '{' or '}')
                {
                    break;
                }

                if (IsBreak() || IsEof())
                {
                    break;
                }

                // Hit whitespace — accumulate it
                if (ch is ' ' or '\t')
                {
                    whitespaces.Append(ch);
                    Advance();
                    continue;
                }

                // Non-whitespace content — flush accumulated whitespace first
                if (hasLeadingBlanks || whitespaces.Length > 0)
                {
                    if (hasLeadingBlanks)
                    {
                        if (leadingBreak.Length > 0 && leadingBreak[0] == '\n')
                        {
                            if (trailingBreaks.Length == 0)
                            {
                                value.Append(' ');
                            }
                            else
                            {
                                value.Append(trailingBreaks);
                            }
                        }
                        else
                        {
                            value.Append(leadingBreak);
                            value.Append(trailingBreaks);
                        }

                        leadingBreak.Length = 0;
                        trailingBreaks.Length = 0;
                        hasLeadingBlanks = false;
                    }
                    else
                    {
                        value.Append(whitespaces);
                    }

                    whitespaces.Length = 0;
                }

                value.Append(ch);
                Advance();
            }

            // Is it the end?
            if (IsEof() || IsBreak())
            {
                // do nothing yet
            }
            else
            {
                break; // hit a non-continuable char
            }

            // Consume whitespace/line breaks
            if (!IsEof() && IsBreak())
            {
                whitespaces.Length = 0;
                leadingBreak.Append(ReadLine());
                hasLeadingBlanks = true;

                // Consume trailing empty lines (including lines with only whitespace)
                while (!IsEof())
                {
                    if (IsBreak())
                    {
                        trailingBreaks.Append(ReadLine());
                    }
                    else if (Peek() is ' ' or '\t')
                    {
                        // Check if this whitespace-only line is followed by a break
                        var savedPos = _position;
                        var savedCol = _column;
                        while (!IsEof() && Peek() is ' ' or '\t')
                        {
                            Advance();
                        }

                        if (!IsEof() && IsBreak())
                        {
                            trailingBreaks.Append(ReadLine());
                        }
                        else
                        {
                            // Not an empty line — restore position
                            _position = savedPos;
                            _column = savedCol;
                            break;
                        }
                    }
                    else
                    {
                        break;
                    }
                }

                // Check indentation
                while (!IsEof() && Peek() == ' ' && _column < startIndent)
                {
                    Advance();
                }
            }
            else
            {
                break;
            }

            // Check if we're still in valid content territory
            if (_flowLevel == 0 && _column < startIndent)
            {
                break;
            }

            // Check for document indicators at the start of a line
            if (_column == 0 && (
                (Check('-', 0) && Check('-', 1) && Check('-', 2) && IsWhiteBreakOrEof(3)) ||
                (Check('.', 0) && Check('.', 1) && Check('.', 2) && IsWhiteBreakOrEof(3))))
            {
                break;
            }
        }

        return value.ToString();
    }

    // -----------------------------------------------------------------------
    // Simple key tracking
    // -----------------------------------------------------------------------

    private void SaveSimpleKey()
    {
        var isRequired = _flowLevel == 0 && _indent == _column;

        if (_simpleKeyAllowed)
        {
            RemoveSimpleKey();
            _simpleKeys.Pop();
            _simpleKeys.Push(new SimpleKeyState
            {
                IsPossible = true,
                IsRequired = isRequired,
                TokenNumber = _tokensParsed + (_tokens.Count - _tokenHead),
                Mark = CurrentMark,
            });
        }
    }

    private void RemoveSimpleKey()
    {
        if (_simpleKeys.Count > 0)
        {
            var key = _simpleKeys.Peek();
            if (key.IsPossible && key.IsRequired)
            {
                throw new YamlException($"Could not find expected ':' at {CurrentMark}.");
            }

            if (key.IsPossible)
            {
                _simpleKeys.Pop();
                _simpleKeys.Push(SimpleKeyState.Impossible);
            }
        }
    }

    private void StaleSimpleKeys()
    {
        // Simple keys can't span multiple lines in block context
        // and can't be longer than 1024 characters (per YAML spec)
        // Use the internal array of the stack to avoid allocation
        if (_simpleKeys.Count == 0) return;

        Span<SimpleKeyState> keys = stackalloc SimpleKeyState[_simpleKeys.Count];
        var count = 0;
        foreach (var sk in _simpleKeys)
        {
            keys[count++] = sk;
        }

        var modified = false;
        // Stack enumerates top-to-bottom; we need to update entries
        for (int i = 0; i < count; i++)
        {
            ref var sk = ref keys[i];
            if (sk.IsPossible &&
                (sk.Mark.Line < _line || sk.Mark.Index + 1024 < _position))
            {
                if (sk.IsRequired)
                {
                    throw new YamlException($"While scanning a simple key at {sk.Mark}: could not find expected ':'");
                }

                sk.IsPossible = false;
                modified = true;
            }
        }

        if (modified)
        {
            _simpleKeys.Clear();
            // Push back in reverse order (bottom-to-top) to restore stack order
            for (int i = count - 1; i >= 0; i--)
            {
                _simpleKeys.Push(keys[i]);
            }
        }
    }

    // -----------------------------------------------------------------------
    // Indent tracking
    // -----------------------------------------------------------------------

    private void RollIndent(int column, YamlScannerTokenType type, YamlMark mark, int insertAt = -1)
    {
        if (_flowLevel > 0)
        {
            return;
        }

        if (_indent < column)
        {
            _indents.Push(_indent);
            _indent = column;

            var token = new YamlScannerToken(type, mark, mark);
            if (insertAt >= 0)
            {
                InsertToken(insertAt, token);
            }
            else
            {
                _tokens.Add(token);
            }
        }
    }

    private void UnrollIndent(int column)
    {
        if (_flowLevel > 0)
        {
            return;
        }

        while (_indent > column)
        {
            var mark = CurrentMark;
            _indent = _indents.Pop();
            _tokens.Add(new YamlScannerToken(YamlScannerTokenType.BlockEnd, mark, mark));
        }
    }

    private void IncreaseFlowLevel()
    {
        _flowLevel++;
        _simpleKeys.Push(SimpleKeyState.Impossible);
    }

    private void DecreaseFlowLevel()
    {
        if (_flowLevel > 0)
        {
            _flowLevel--;
            _simpleKeys.Pop();
        }
    }

    // -----------------------------------------------------------------------
    // Token queue helpers
    // -----------------------------------------------------------------------

    private void InsertToken(int offset, YamlScannerToken token)
    {
        // offset is relative to _tokenHead (the "front" of unread tokens)
        var insertIndex = _tokenHead + offset;
        if (insertIndex < _tokenHead) insertIndex = _tokenHead;
        if (insertIndex > _tokens.Count) insertIndex = _tokens.Count;
        _tokens.Insert(insertIndex, token);
    }

    // -----------------------------------------------------------------------
    // Character-level helpers
    // -----------------------------------------------------------------------

    private void ScanToNextToken()
    {
        var guard = 0;
        while (true)
        {
            if (++guard > 1000000)
            {
                throw new YamlException($"ScanToNextToken exceeded maximum iterations at {CurrentMark}.");
            }

            // Skip whitespace (spaces and tabs as separation)
            while (!IsEof() && (Peek() == ' ' || Peek() == '\t'))
            {
                Advance();
            }

            // Skip comments
            if (!IsEof() && Peek() == '#')
            {
                while (!IsEof() && !IsBreak())
                {
                    Advance();
                }
            }

            // Skip line breaks
            if (!IsEof() && IsBreak())
            {
                AdvanceLine();
                if (_flowLevel == 0)
                {
                    _simpleKeyAllowed = true;
                }
            }
            else
            {
                break;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private char Peek() => _position < _input.Length ? _input[_position] : '\0';

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private char PeekAt(int offset)
    {
        var idx = _position + offset;
        return (uint)idx < (uint)_input.Length ? _input[idx] : '\0';
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsEof() => _position >= _input.Length;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool Check(char expected, int offset)
    {
        var idx = _position + offset;
        return (uint)idx < (uint)_input.Length && _input[idx] == expected;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Advance()
    {
        _position++;
        _column++;
    }

    private void Advance(int count)
    {
        _position += count;
        _column += count;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AdvanceLine()
    {
        // CRLF already normalized to LF in constructor
        _position++;
        _line++;
        _column = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private char ReadLine()
    {
        AdvanceLine();
        return '\n';
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsBreak() => _position < _input.Length && _input[_position] is '\n';

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsWhite() => _position < _input.Length && _input[_position] is ' ' or '\t';

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsWhiteOrBreak() => _position < _input.Length && _input[_position] is ' ' or '\t' or '\n';

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsWhiteAt(int offset)
    {
        var idx = _position + offset;
        return (uint)idx < (uint)_input.Length && _input[idx] is ' ' or '\t';
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsWhiteBreakOrEof(int offset)
    {
        var idx = _position + offset;
        if ((uint)idx >= (uint)_input.Length) return true;
        return _input[idx] is ' ' or '\t' or '\n';
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsPeekFlowIndicator(int offset)
    {
        var idx = _position + offset;
        if ((uint)idx >= (uint)_input.Length) return true;
        return _input[idx] is ' ' or '\t' or '\n' or ',' or '[' or ']' or '{' or '}';
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsAnchorChar(char ch) =>
        ch is not (' ' or '\t' or '\n' or '\0' or ',' or '[' or ']' or '{' or '}');

    private bool IsPlainScalarStart(char ch)
    {
        if (IsWhiteOrBreak() || IsEof())
        {
            return false;
        }

        if (ch is ',' or '[' or ']' or '{' or '}')
        {
            return false;
        }

        if (ch == '-' && IsWhiteBreakOrEof(1))
        {
            return false;
        }

        if (ch == '?' && IsWhiteBreakOrEof(1))
        {
            return false;
        }

        if (ch == ':' && IsWhiteBreakOrEof(1))
        {
            return false;
        }

        if (ch is '#' && _position > 0 && IsWhiteAt(-1))
        {
            return false;
        }

        return true;
    }
}
