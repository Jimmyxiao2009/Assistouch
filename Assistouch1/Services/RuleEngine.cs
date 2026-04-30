using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using AssistiveTouch.Models;

namespace AssistiveTouch.Services;

public class RuleEngine
{
    private readonly ConfigService _config;

    public RuleEngine(ConfigService config)
    {
        _config = config;
    }

    /// <summary>
    /// Returns all recommended actions whose rules match the given foreground window info.
    /// </summary>
    public List<ActionItem> GetRecommendedActions(ForegroundWindowInfo info)
    {
        var result = new List<ActionItem>();
        foreach (var rule in _config.Config.Rules)
        {
            if (!rule.Enabled) continue;
            if (Matches(rule, info))
                result.AddRange(rule.RecommendedActions);
        }
        return result;
    }

    private static bool Matches(AppRule rule, ForegroundWindowInfo info)
    {
        // All matchers must pass (AND logic within a rule)
        foreach (var m in rule.Matchers)
        {
            if (!MatchOne(m, info)) return false;
        }
        return rule.Matchers.Count > 0;
    }

    private static bool MatchOne(RuleMatcher m, ForegroundWindowInfo info)
    {
        return m.Type switch
        {
            AssistiveTouch.Models.MatchType.ProcessName =>
                string.Equals(info.ProcessName, m.Value, StringComparison.OrdinalIgnoreCase),

            AssistiveTouch.Models.MatchType.ExeName =>
                string.Equals(System.IO.Path.GetFileName(info.ExeFullPath), m.Value, StringComparison.OrdinalIgnoreCase),

            AssistiveTouch.Models.MatchType.Pid =>
                uint.TryParse(m.Value, out var pid) && pid == info.Pid,

            AssistiveTouch.Models.MatchType.ProcessNameRegex =>
                TryRegex(m.Value, info.ProcessName),

            AssistiveTouch.Models.MatchType.WindowTitleRegex =>
                TryRegex(m.Value, info.WindowTitle),

            _ => false
        };
    }

    private static bool TryRegex(string pattern, string input)
    {
        try { return Regex.IsMatch(input, pattern, RegexOptions.IgnoreCase); }
        catch { return false; }
    }
}
