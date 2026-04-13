using System.Globalization;

namespace System.Text.Yaml;

/// <summary>
/// Composes a stream of scanner tokens into a native YamlNode tree.
/// Replaces ScannerComposer (which targeted JsonNode).
/// </summary>
internal sealed class YamlNodeComposer
{
    private readonly Utf8YamlScanner _scanner;
    private readonly YamlReaderOptions _options;
    private readonly Dictionary<string, YamlNode?> _anchors = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _tagDirectives = new(StringComparer.Ordinal)
    {
        ["!"] = "!",
        ["!!"] = "tag:yaml.org,2002:"
    };
    private readonly Dictionary<string, string> _declaredTagDirectives = new(StringComparer.Ordinal);
    private string? _yamlVersionDirective;
    private YamlSchema _effectiveSchema;
    private int _resolvedAliasCount;
    private long _totalClonedNodes;
    private YamlScannerToken _current;

    public YamlNodeComposer(string yaml, YamlReaderOptions options)
    {
        _scanner = new Utf8YamlScanner(yaml);
        _options = options;
        _effectiveSchema = options.Schema;
        Advance();
    }

    public List<YamlDocumentParseResult> ComposeAll()
    {
        var results = new List<YamlDocumentParseResult>();

        Expect(YamlScannerTokenType.StreamStart);

        while (_current.Type != YamlScannerTokenType.StreamEnd)
        {
            results.Add(ComposeDocument());
        }

        if (results.Count == 0)
        {
            throw new YamlException("YAML content cannot be empty.");
        }

        return results;
    }

    private YamlDocumentParseResult ComposeDocument()
    {
        _anchors.Clear();
        _resolvedAliasCount = 0;
        _totalClonedNodes = 0;
        _effectiveSchema = _options.Schema;

        // Process directives
        while (_current.Type == YamlScannerTokenType.Tag && _current.Value is not null && _current.Value.StartsWith('%'))
        {
            ProcessDirective(_current.Value);
            Advance();
        }

        if (_current.Type == YamlScannerTokenType.DocumentStart)
        {
            Advance();
        }

        YamlNode? root = null;
        if (_current.Type is not (YamlScannerTokenType.DocumentEnd or YamlScannerTokenType.StreamEnd))
        {
            root = ComposeNode();
        }

        while (_current.Type == YamlScannerTokenType.DocumentEnd)
        {
            Advance();
        }

        return new YamlDocumentParseResult(root, _yamlVersionDirective,
            new Dictionary<string, string>(_declaredTagDirectives, StringComparer.Ordinal), _effectiveSchema);
    }

    // -----------------------------------------------------------------------
    // Node composition
    // -----------------------------------------------------------------------

    private YamlNode? ComposeNode()
    {
        string? anchor = null;
        string? tag = null;

        if (_current.Type == YamlScannerTokenType.Alias)
        {
            var alias = _current.Value;
            Advance();
            return ResolveAlias(alias!);
        }

        while (_current.Type is YamlScannerTokenType.Anchor or YamlScannerTokenType.Tag)
        {
            if (_current.Type == YamlScannerTokenType.Anchor)
                anchor = _current.Value;
            else
                tag = ExpandTag(_current.Value);
            Advance();
        }

        YamlNode? node;

        switch (_current.Type)
        {
            case YamlScannerTokenType.Scalar:
                node = ComposeScalar(tag);
                break;

            case YamlScannerTokenType.BlockSequenceStart:
                node = ComposeBlockSequence();
                break;

            case YamlScannerTokenType.BlockEntry:
                node = ComposeCompactBlockSequence();
                break;

            case YamlScannerTokenType.BlockMappingStart:
                node = ComposeBlockMapping();
                break;

            case YamlScannerTokenType.FlowSequenceStart:
                node = ComposeFlowSequence();
                break;

            case YamlScannerTokenType.FlowMappingStart:
                node = ComposeFlowMapping();
                break;

            default:
                node = null;
                break;
        }

        // Handle tagged or anchored empty node — create a scalar to carry metadata
        if (node is null && (tag is not null || anchor is not null))
        {
            node = new YamlScalarNode(string.Empty) { Tag = tag, Anchor = anchor };
        }

        if (node is not null)
        {
            node.Tag = tag;
            node.Anchor = anchor;
        }

        // Register anchor (even for empty nodes)
        if (anchor is not null)
        {
            _anchors[anchor] = node;
        }

        return node;
    }

    // -----------------------------------------------------------------------
    // Scalars
    // -----------------------------------------------------------------------

    private YamlNode ComposeScalar(string? tag)
    {
        var value = _current.Value ?? string.Empty;
        var style = _current.Style;
        Advance();

        var scalarStyle = style switch
        {
            ScalarStyle.SingleQuoted => YamlScalarStyle.SingleQuoted,
            ScalarStyle.DoubleQuoted => YamlScalarStyle.DoubleQuoted,
            ScalarStyle.Literal => YamlScalarStyle.Literal,
            ScalarStyle.Folded => YamlScalarStyle.Folded,
            _ => YamlScalarStyle.Plain
        };

        return new YamlScalarNode(value, scalarStyle) { Tag = tag };
    }

    // -----------------------------------------------------------------------
    // Block collections
    // -----------------------------------------------------------------------

    private YamlNode ComposeBlockSequence()
    {
        Advance(); // consume BlockSequenceStart

        var seq = new YamlSequenceNode();

        while (_current.Type == YamlScannerTokenType.BlockEntry)
        {
            Advance();

            if (_current.Type is YamlScannerTokenType.BlockEntry or YamlScannerTokenType.BlockEnd)
            {
                seq.Add(null);
            }
            else
            {
                seq.Add(ComposeNode());
            }
        }

        Expect(YamlScannerTokenType.BlockEnd);

        return seq;
    }

    private YamlNode ComposeCompactBlockSequence()
    {
        var seq = new YamlSequenceNode();

        while (_current.Type == YamlScannerTokenType.BlockEntry)
        {
            Advance();

            if (_current.Type is YamlScannerTokenType.BlockEntry or YamlScannerTokenType.Key
                or YamlScannerTokenType.BlockEnd or YamlScannerTokenType.DocumentEnd
                or YamlScannerTokenType.StreamEnd)
            {
                seq.Add(null);
            }
            else
            {
                seq.Add(ComposeNode());
            }
        }

        return seq;
    }

    private YamlNode ComposeBlockMapping()
    {
        Advance(); // consume BlockMappingStart

        var map = new YamlMappingNode();

        while (_current.Type == YamlScannerTokenType.Key)
        {
            Advance(); // consume Key

            YamlNode? keyNode;
            if (_current.Type is YamlScannerTokenType.Value or YamlScannerTokenType.Key or YamlScannerTokenType.BlockEnd)
            {
                keyNode = new YamlScalarNode(string.Empty);
            }
            else
            {
                keyNode = ComposeNode();
            }

            YamlNode? value = null;
            if (_current.Type == YamlScannerTokenType.Value)
            {
                Advance();

                if (_current.Type is not (YamlScannerTokenType.Key or YamlScannerTokenType.BlockEnd))
                {
                    value = ComposeNode();
                }
            }

            // Handle merge keys
            if (_options.AllowMergeKeys && keyNode is YamlScalarNode { Value: "<<" } && value is not null)
            {
                MergeIntoMapping(map, value);
            }
            else
            {
                AddToMapping(map, keyNode ?? new YamlScalarNode(string.Empty), value);
            }
        }

        Expect(YamlScannerTokenType.BlockEnd);

        return map;
    }

    // -----------------------------------------------------------------------
    // Flow collections
    // -----------------------------------------------------------------------

    private YamlNode ComposeFlowSequence()
    {
        Advance(); // consume FlowSequenceStart

        var seq = new YamlSequenceNode();

        while (_current.Type != YamlScannerTokenType.FlowSequenceEnd)
        {
            if (_current.Type == YamlScannerTokenType.FlowEntry)
            {
                Advance();
                continue;
            }

            if (_current.Type == YamlScannerTokenType.Key)
            {
                var pair = ComposeFlowMappingBody(singlePair: true);
                seq.Add(pair);
            }
            else
            {
                var item = ComposeNode();

                if (_current.Type == YamlScannerTokenType.Value)
                {
                    Advance();
                    var keyNode = item ?? new YamlScalarNode(string.Empty);
                    YamlNode? pairValue = null;
                    if (_current.Type is not (YamlScannerTokenType.FlowEntry or YamlScannerTokenType.FlowSequenceEnd))
                    {
                        pairValue = ComposeNode();
                    }

                    var pair = new YamlMappingNode();
                    AddToMapping(pair, keyNode, pairValue);
                    seq.Add(pair);
                }
                else
                {
                    seq.Add(item);
                }
            }
        }

        Advance(); // consume FlowSequenceEnd
        return seq;
    }

    private YamlNode ComposeFlowMapping()
    {
        Advance(); // consume FlowMappingStart
        var map = ComposeFlowMappingBody(singlePair: false);
        Advance(); // consume FlowMappingEnd
        return map;
    }

    private YamlMappingNode ComposeFlowMappingBody(bool singlePair)
    {
        var map = new YamlMappingNode();
        var endType = singlePair ? YamlScannerTokenType.FlowEntry : YamlScannerTokenType.FlowMappingEnd;
        var altEnd = singlePair ? YamlScannerTokenType.FlowSequenceEnd : YamlScannerTokenType.FlowMappingEnd;

        while (_current.Type != endType && _current.Type != altEnd)
        {
            if (_current.Type == YamlScannerTokenType.FlowEntry)
            {
                Advance();
                continue;
            }

            YamlNode? keyNode;
            if (_current.Type == YamlScannerTokenType.Key)
            {
                Advance();

                if (_current.Type is YamlScannerTokenType.Value or YamlScannerTokenType.FlowEntry
                    or YamlScannerTokenType.FlowMappingEnd or YamlScannerTokenType.FlowSequenceEnd)
                {
                    keyNode = new YamlScalarNode(string.Empty);
                }
                else
                {
                    keyNode = ComposeNode();
                }
            }
            else
            {
                keyNode = ComposeNode();
            }

            YamlNode? value = null;
            if (_current.Type == YamlScannerTokenType.Value)
            {
                Advance();

                if (_current.Type is not (YamlScannerTokenType.FlowEntry or YamlScannerTokenType.FlowMappingEnd
                    or YamlScannerTokenType.FlowSequenceEnd))
                {
                    value = ComposeNode();
                }
            }

            if (_options.AllowMergeKeys && keyNode is YamlScalarNode { Value: "<<" } && value is not null)
            {
                MergeIntoMapping(map, value);
            }
            else
            {
                AddToMapping(map, keyNode ?? new YamlScalarNode(string.Empty), value);
            }

            if (singlePair) break;
        }

        return map;
    }

    // -----------------------------------------------------------------------
    // Mapping helpers
    // -----------------------------------------------------------------------

    private void AddToMapping(YamlMappingNode mapping, YamlNode keyNode, YamlNode? value)
    {
        var keyString = YamlMappingNode.GetStringKey(keyNode);

        if (_options.DuplicateKeyHandling == YamlDuplicateKeyHandling.Disallow && mapping.ContainsKey(keyString))
        {
            throw new YamlException($"Duplicate YAML mapping key '{keyString}'.");
        }

        if (keyNode is not YamlScalarNode)
        {
            mapping.HasComplexKeys = true;
        }

        // Last-wins: replace existing key if present
        for (int i = 0; i < mapping.Entries.Count; i++)
        {
            if (mapping.Entries[i].Key is YamlScalarNode existing && existing.Value == keyString)
            {
                mapping.Entries[i] = new KeyValuePair<YamlNode, YamlNode?>(keyNode, value);
                return;
            }
        }

        mapping.Add(keyNode, value);
    }

    private void MergeIntoMapping(YamlMappingNode mapping, YamlNode mergeValue)
    {
        if (mergeValue is YamlMappingNode mergeMap)
        {
            foreach (var entry in mergeMap.Entries)
            {
                var key = YamlMappingNode.GetStringKey(entry.Key);
                if (!mapping.ContainsKey(key))
                {
                    mapping.Add(entry.Key, entry.Value);
                }
            }
        }
        else if (mergeValue is YamlSequenceNode mergeSeq)
        {
            foreach (var item in mergeSeq.Children)
            {
                if (item is YamlMappingNode itemMap)
                {
                    foreach (var entry in itemMap.Entries)
                    {
                        var key = YamlMappingNode.GetStringKey(entry.Key);
                        if (!mapping.ContainsKey(key))
                        {
                            mapping.Add(entry.Key, entry.Value);
                        }
                    }
                }
            }
        }
    }

    // -----------------------------------------------------------------------
    // Alias resolution
    // -----------------------------------------------------------------------

    private YamlNode? ResolveAlias(string alias)
    {
        _resolvedAliasCount++;
        if (_resolvedAliasCount > _options.MaxAliasCount)
        {
            throw new YamlException($"Alias resolution exceeded maximum of {_options.MaxAliasCount}.");
        }

        if (!_anchors.TryGetValue(alias, out var value))
        {
            throw new YamlException($"Unknown alias '*{alias}'.");
        }

        var clone = CloneNode(value);
        if (clone is not null)
        {
            clone.Alias = alias;
        }

        return clone;
    }

    private YamlNode? CloneNode(YamlNode? node)
    {
        _totalClonedNodes++;
        if (_totalClonedNodes > _options.MaxAliasExpansionNodes)
        {
            throw new YamlException($"Alias expansion exceeded maximum of {_options.MaxAliasExpansionNodes} nodes.");
        }

        if (node is null) return null;

        return node switch
        {
            YamlScalarNode s => new YamlScalarNode(s.Value, s.Style) { Tag = s.Tag, Anchor = null },
            YamlSequenceNode seq => CloneSequence(seq),
            YamlMappingNode map => CloneMapping(map),
            _ => null
        };
    }

    private YamlSequenceNode CloneSequence(YamlSequenceNode seq)
    {
        var clone = new YamlSequenceNode { Tag = seq.Tag };
        foreach (var item in seq.Children)
        {
            clone.Add(CloneNode(item));
        }
        return clone;
    }

    private YamlMappingNode CloneMapping(YamlMappingNode map)
    {
        var clone = new YamlMappingNode { Tag = map.Tag, HasComplexKeys = map.HasComplexKeys };
        foreach (var entry in map.Entries)
        {
            clone.Add(CloneNode(entry.Key)!, CloneNode(entry.Value));
        }
        return clone;
    }

    // -----------------------------------------------------------------------
    // Tag expansion
    // -----------------------------------------------------------------------

    private string? ExpandTag(string? rawTag)
    {
        if (rawTag is null) return null;
        var tag = rawTag.Trim();
        if (tag.Length == 0) return null;

        if (tag.StartsWith("!<") && tag.EndsWith(">"))
        {
            return tag[2..^1];
        }

        var handleEnd = tag.IndexOf('!', 1);
        if (handleEnd > 0)
        {
            var handle = tag[..(handleEnd + 1)];
            var suffix = tag[(handleEnd + 1)..];
            if (_tagDirectives.TryGetValue(handle, out var prefix))
            {
                return prefix + suffix;
            }
        }

        if (_tagDirectives.TryGetValue("!", out var primaryPrefix) && tag.Length > 1)
        {
            return primaryPrefix + tag[1..];
        }

        return tag;
    }

    // -----------------------------------------------------------------------
    // Directives
    // -----------------------------------------------------------------------

    private void ProcessDirective(string directiveLine)
    {
        var parts = directiveLine.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return;

        switch (parts[0])
        {
            case "%YAML":
                if (parts.Length >= 2)
                {
                    _yamlVersionDirective = parts[1];
                    var version = parts[1].Split('.', 2);
                    if (version.Length == 2 && version[0] == "1" && version[1] == "1" && _options.Schema == YamlSchema.Core)
                    {
                        _effectiveSchema = YamlSchema.Yaml11;
                    }
                }
                break;
            case "%TAG":
                if (parts.Length >= 3)
                {
                    _tagDirectives[parts[1]] = parts[2];
                    _declaredTagDirectives[parts[1]] = parts[2];
                }
                break;
        }
    }

    // -----------------------------------------------------------------------
    // Token helpers
    // -----------------------------------------------------------------------

    private void Advance()
    {
        if (!_scanner.MoveNext())
        {
            throw new YamlException("Unexpected end of YAML token stream.");
        }

        _current = _scanner.Current!.Value;
    }

    private void Expect(YamlScannerTokenType type)
    {
        if (_current.Type != type)
        {
            throw new YamlException($"Expected {type} but found {_current.Type} at {_current.Start}.");
        }

        Advance();
    }
}
