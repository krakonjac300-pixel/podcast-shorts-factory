using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using SecondCursor.Core.Game;

namespace SecondCursor.Core.Story
{
    /// <summary>
    /// Phase Q2 (board C V1, V3, V6): the one gate every echo of the player's own words goes through before the game shows it
    /// again (a file, a mail, her voice, an ending, the Retention Record, the demo's handoff). Slurs, the strongest profanity and
    /// anything that looks personal (an e-mail address, a web address, a phone-length number, a street address) never come
    /// back: a file shows <see cref="RedactedFile"/>, her voice <see cref="RedactedVoice"/>. Ordinary swearing stays ("make me" and
    /// "fuck you" are funny when quoted back). Engine-free, so the rules are unit-tested.
    /// </summary>
    public static class EchoFilter
    {
        public const string RedactedFile = "[REDACTED BY RETENTION]";
        public const string RedactedVoice = "REDACTED";
        /// <summary>Her voice quotes at most this many characters, cut at a word boundary.</summary>
        public const int VoiceMax = 28;

        /// <summary>
        /// The blocklist in ROT13 (so the source can be read without reading the list). "=" first: whole words only (so "spicy",
        /// "raccoon" and "grape" pass); the others also match inside a word and across spaced-out letters ("n i g ...").
        /// Repeated letters are allowed ("faaag"), and common digit and symbol stand-ins are read as letters.
        /// </summary>
        static readonly string[] Rot13 =
        {
            "avttre", "avttn", "=avttnf", "fnaqavttre", "=puvax", "=tbbx", "=fcvp", "=fcvpx", "jrgonpx", "=xvxr", "=ornare", "=pbba",
            "=cnxv", "enturnq", "gbjryurnq", "snttbg", "=snt", "=sntf", "=qlxr", "genaal", "furznyr", "ergneq", "=encr", "=encrq",
            "=encvfg", "=encrf", "=phag", "=phagf", "=xlf", "xvyy lbhefrys", "xvyylbhefrys", "uvgyre", "=anmv", "=anmvf", "=urvy",
            "=xxx", "juvgr cbjre", "tnf gur wrjf", "crqbcuvyr", "=crqb", "=cnrqb", "cnrqbcuvyr", "zbyrfg",
        };

        static List<Regex> _whole, _inside;

        static readonly Regex Email = new Regex(@"\S+@\S+\.\S+", RegexOptions.CultureInvariant);
        static readonly Regex Web = new Regex(@"(https?://|www\.|[a-z0-9]\.(com|net|org|tv|gg|io|me|co)\b)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        static readonly Regex Street = new Regex(@"\b\d+\s+\w+\s+(street|st|avenue|ave|road|rd|lane|ln|drive|dr|blvd|boulevard|court|ct|way)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        /// <summary>A phone number, an ID or a card is at least this many digits in one line.</summary>
        const int PersonalDigits = 7;

        static void Build()
        {
            if (_whole != null) return;
            var whole = new List<Regex>();
            var inside = new List<Regex>();
            foreach (var entry in Rot13)
            {
                bool w = entry[0] == '=';
                string plain = Rot(w ? entry.Substring(1) : entry);
                string pattern = RunPattern(plain);
                if (w) whole.Add(new Regex(@"(^| )" + pattern + "s?( |$)", RegexOptions.CultureInvariant));
                else inside.Add(new Regex(pattern, RegexOptions.CultureInvariant));
            }
            _inside = inside;
            _whole = whole;
        }

        static string Rot(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
                sb.Append(c >= 'a' && c <= 'z' ? (char)('a' + (c - 'a' + 13) % 26) : c);
            return sb.ToString();
        }

        /// <summary>"faggot" becomes "f+a+g{2,}o+t+" (a run may repeat, never shrink); spaces in a phrase become optional gaps.</summary>
        static string RunPattern(string word)
        {
            var sb = new StringBuilder();
            int i = 0;
            while (i < word.Length)
            {
                char c = word[i];
                int n = 1;
                while (i + n < word.Length && word[i + n] == c) n++;
                if (c == ' ') sb.Append(" ?");
                else sb.Append(Regex.Escape(c.ToString())).Append(n > 1 ? "{" + n + ",}" : "+");
                i += n;
            }
            return sb.ToString();
        }

        /// <summary>Lower case, stand-ins read as letters (0 o, 1 i, 3 e, 4 a, 5 s, 7 t, @ a, $ s, ! i), everything else a single space.</summary>
        static string Letters(string text) => Letters(text, true);

        /// <param name="symbolsAsLetters">False: "!" and "|" separate words (so "kys!" is still the whole word "kys").</param>
        static string Letters(string text, bool symbolsAsLetters)
        {
            var sb = new StringBuilder(text.Length);
            bool space = true;
            foreach (char raw in text)
            {
                char c = char.ToLowerInvariant(raw);
                switch (c)
                {
                    case '0': c = 'o'; break;
                    case '1': c = 'i'; break;
                    case '!': case '|': c = symbolsAsLetters ? 'i' : ' '; break;
                    case '3': c = 'e'; break;
                    case '4': case '@': c = 'a'; break;
                    case '5': case '$': c = 's'; break;
                    case '7': c = 't'; break;
                }
                if (c >= 'a' && c <= 'z')
                {
                    sb.Append(c);
                    space = false;
                }
                else if (c != '\'' && !space)
                {
                    sb.Append(' ');
                    space = true;
                }
            }
            return sb.ToString().Trim();
        }

        /// <summary>True if the text holds a blocked word (whole words, inside words, or spelled out across spaces).</summary>
        public static bool IsBlocked(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            Build();
            string letters = Letters(text);
            if (letters.Length == 0) return false;
            string joined = letters.Replace(" ", "");
            string words = Letters(text, false);
            foreach (var r in _whole) if (r.IsMatch(letters) || r.IsMatch(words)) return true;
            foreach (var r in _inside) if (r.IsMatch(letters) || r.IsMatch(joined)) return true;
            return false;
        }

        /// <summary>True if the text looks personal: an e-mail or web address, a street address, or seven or more digits.</summary>
        public static bool IsPersonal(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            int digits = 0;
            foreach (char c in text) if (c >= '0' && c <= '9') digits++;
            return digits >= PersonalDigits || Email.IsMatch(text) || Web.IsMatch(text) || Street.IsMatch(text);
        }

        /// <summary>Blocked or personal: never echoed.</summary>
        public static bool Withheld(string text) => IsBlocked(text) || IsPersonal(text);

        /// <summary>The line as the player typed it (made safe: <see cref="SaveData.SanitizePlayerLine"/>), or <see cref="RedactedFile"/>; "" stays "".</summary>
        public static string ForFile(string line)
        {
            string s = SaveData.SanitizePlayerLine(line);
            if (s.Length == 0) return "";
            return Withheld(s) ? RedactedFile : s;
        }

        /// <summary>
        /// The line in her voice: capitals, letters, digits and single spaces only (her lines never carry punctuation), cut at a word
        /// boundary to <see cref="VoiceMax"/> characters; <see cref="RedactedVoice"/> if it is withheld; "" stays "".
        /// </summary>
        public static string ForVoice(string line)
        {
            string s = SaveData.SanitizePlayerLine(line);
            if (s.Length == 0) return "";
            if (Withheld(s)) return RedactedVoice;
            var sb = new StringBuilder(s.Length);
            bool space = true;
            foreach (char raw in s)
            {
                if (raw == '\'' || raw == '`') continue;
                char c = char.ToUpperInvariant(raw);
                if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'))
                {
                    sb.Append(c);
                    space = false;
                }
                else if (!space)
                {
                    sb.Append(' ');
                    space = true;
                }
            }
            string voice = sb.ToString().Trim();
            if (voice.Length <= VoiceMax) return voice;
            int cut = voice.LastIndexOf(' ', VoiceMax);
            return (cut > 0 ? voice.Substring(0, cut) : voice.Substring(0, VoiceMax)).TrimEnd();
        }
    }
}
