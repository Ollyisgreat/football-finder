using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace XtreamFootball.Services
{
    /// <summary>
    /// Accent/case/punctuation-insensitive whole-word matching of team names.
    /// </summary>
    public static class TeamMatcher
    {
        // Groups of names that mean the same club. Searching any one finds all of them.
        private static readonly string[][] AliasGroups =
        {
            new[] { "manchester united", "man utd", "man united", "manchester utd" },
            new[] { "manchester city", "man city" },
            new[] { "tottenham", "tottenham hotspur", "spurs" },
            new[] { "wolves", "wolverhampton", "wolverhampton wanderers" },
            new[] { "newcastle", "newcastle united", "newcastle utd" },
            new[] { "nottingham forest", "nottm forest", "notts forest", "nott m forest" },
            new[] { "sheffield united", "sheffield utd", "sheff utd" },
            new[] { "sheffield wednesday", "sheff wed", "sheffield wed" },
            new[] { "west ham", "west ham united", "west ham utd" },
            new[] { "leeds", "leeds united", "leeds utd" },
            new[] { "brighton", "brighton hove albion", "brighton and hove albion" },
            new[] { "qpr", "queens park rangers" },
            new[] { "west brom", "west bromwich", "west bromwich albion", "wba" },
            new[] { "psg", "paris saint germain", "paris sg", "paris st germain" },
            new[] { "bayern munich", "bayern munchen", "bayern muenchen", "fc bayern", "bayern" },
            new[] { "borussia dortmund", "dortmund", "bvb" },
            new[] { "bayer leverkusen", "leverkusen" },
            new[] { "rb leipzig", "leipzig" },
            new[] { "inter milan", "internazionale", "inter" },
            new[] { "ac milan", "milan" },
            new[] { "juventus", "juve" },
            new[] { "atletico madrid", "atletico", "atl madrid", "atletico de madrid" },
            new[] { "athletic bilbao", "athletic club" },
            new[] { "sporting cp", "sporting lisbon", "sporting" },
            new[] { "fenerbahce", "fener" },
            new[] { "galatasaray", "gala" },
            new[] { "besiktas", "bjk" },
            new[] { "trabzonspor", "trabzon" },
            new[] { "ajax", "ajax amsterdam" },
            new[] { "psv", "psv eindhoven" },
            new[] { "olympiacos", "olympiakos" },
            new[] { "red star belgrade", "crvena zvezda" },
            new[] { "usa", "united states", "usmnt" },
        };

        /// <summary>
        /// Lowercases, strips accents, turns every non letter/digit into a space and pads with spaces,
        /// so " term " can be found with a plain Contains for whole-word matching.
        /// </summary>
        public static string Normalize(string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return " ";
            }

            var decomposed = text.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(decomposed.Length + 2);
            sb.Append(' ');
            var lastWasSpace = true;
            foreach (var c in decomposed)
            {
                var category = CharUnicodeInfo.GetUnicodeCategory(c);
                if (category == UnicodeCategory.NonSpacingMark)
                {
                    continue;
                }

                if (c == '&')
                {
                    // "Brighton & Hove" == "Brighton and Hove" is handled by alias; just drop '&'
                    if (!lastWasSpace)
                    {
                        sb.Append(' ');
                        lastWasSpace = true;
                    }

                    continue;
                }

                if (char.IsLetterOrDigit(c))
                {
                    var lower = char.ToLowerInvariant(c);
                    if (lower == 'ı')
                    {
                        lower = 'i'; // Turkish dotless i
                    }

                    sb.Append(lower);
                    lastWasSpace = false;
                }
                else if (!lastWasSpace)
                {
                    sb.Append(' ');
                    lastWasSpace = true;
                }
            }

            if (!lastWasSpace)
            {
                sb.Append(' ');
            }

            return sb.ToString();
        }

        /// <summary>
        /// Turns user input ("Arsenal", "Man Utd|Manchester United", "Arsenal, Chelsea") into padded search terms.
        /// </summary>
        public static List<string> ExpandTerms(string? input)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            if (string.IsNullOrWhiteSpace(input))
            {
                return new List<string>();
            }

            var raw = input.Split(new[] { '|', ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in raw)
            {
                var padded = Normalize(part);
                var bare = padded.Trim();
                if (bare.Length < 2)
                {
                    continue;
                }

                result.Add(padded);
                foreach (var group in AliasGroups)
                {
                    if (group.Contains(bare, StringComparer.Ordinal))
                    {
                        foreach (var alias in group)
                        {
                            result.Add(" " + alias + " ");
                        }
                    }
                }
            }

            return result.ToList();
        }

        /// <summary>Splits a multi-line team list into one search query per line.</summary>
        public static List<string> SplitTeamLines(string? teams)
        {
            if (string.IsNullOrWhiteSpace(teams))
            {
                return new List<string>();
            }

            return teams
                .Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0)
                .ToList();
        }

        public static List<string> SplitCsv(string? csv)
        {
            if (string.IsNullOrWhiteSpace(csv))
            {
                return new List<string>();
            }

            return csv
                .Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => Normalize(s).Trim())
                .Where(s => s.Length > 0)
                .ToList();
        }

        public static bool MatchesAny(string normalizedText, IReadOnlyList<string> paddedTerms)
        {
            for (var i = 0; i < paddedTerms.Count; i++)
            {
                if (normalizedText.Contains(paddedTerms[i], StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Substring (not whole word) check, used for category and exclude filters.</summary>
        public static bool ContainsAny(string normalizedText, IReadOnlyList<string> bareWords)
        {
            for (var i = 0; i < bareWords.Count; i++)
            {
                if (normalizedText.Contains(bareWords[i], StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
