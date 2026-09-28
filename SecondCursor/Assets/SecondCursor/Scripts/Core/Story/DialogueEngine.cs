using System;
using System.Collections.Generic;
using System.Text;
using SecondCursor.Core.Content;

namespace SecondCursor.Core.Story
{
    public sealed class DialogueReply
    {
        public string[] Lines = Array.Empty<string>();
        public string MatchedKeyword;
        public int ResponseIndex = -1;
        public bool IsFallback;
        /// <summary>Broad category of what the player said, used for story flags / entity memory.</summary>
        public string Category = "other";
    }

    /// <summary>
    /// Keyword-driven replies for the Notepad conversation. No AI: the player's free text is normalized and
    /// matched against authored keyword lists. Matching rules (to avoid "hi" matching "this"):
    ///  - keywords containing a space: substring match on the normalized sentence;
    ///  - short single words (&lt;= 3 letters): must equal a whole word (or the word + "s");
    ///  - longer single words: match any word containing them ("fuck" matches "fucking").
    /// </summary>
    public sealed class DialogueEngine
    {
        readonly ContentDatabase _content;

        static readonly string[][] Categories =
        {
            new[] { "swear", "fuck", "shit", "wtf", "damn", "hell", "bitch", "crap" },
            new[] { "who", "who", "what are you", "whats that", "what is this", "name", "whos" },
            new[] { "why", "why", "reason" },
            new[] { "refuse", "no", "wont", "never", "leave me", "go away", "fuck off", "stop" },
            new[] { "agree", "yes", "ok", "okay", "fine", "sure", "alright" },
            new[] { "help", "help", "save", "please" },
        };

        public DialogueEngine(ContentDatabase content)
        {
            _content = content;
        }

        public ExchangeData Get(string id) => _content.Exchange(id);

        public DialogueReply Respond(ExchangeData exchange, string playerInput)
        {
            var reply = new DialogueReply();
            string norm = Normalize(playerInput);
            reply.Category = Categorize(norm);
            if (exchange == null) return reply;

            for (int i = 0; i < exchange.responses.Length; i++)
            {
                var r = exchange.responses[i];
                if (r == null) continue;
                foreach (var kw in r.keywords)
                {
                    if (Matches(norm, kw))
                    {
                        reply.Lines = r.reply;
                        reply.MatchedKeyword = kw;
                        reply.ResponseIndex = i;
                        return reply;
                    }
                }
            }
            reply.Lines = exchange.fallback;
            reply.IsFallback = true;
            return reply;
        }

        public static string Categorize(string normalized)
        {
            foreach (var cat in Categories)
                for (int i = 1; i < cat.Length; i++)
                    if (Matches(normalized, cat[i])) return cat[0];
            return "other";
        }

        /// <summary>Lowercase, apostrophes removed, other non-alphanumerics become spaces, runs collapsed.</summary>
        public static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            bool lastSpace = true;
            foreach (char raw in s)
            {
                char c = char.ToLowerInvariant(raw);
                if (c == '\'' || c == '`') continue;
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
                {
                    sb.Append(c);
                    lastSpace = false;
                }
                else if (!lastSpace)
                {
                    sb.Append(' ');
                    lastSpace = true;
                }
            }
            return sb.ToString().Trim();
        }

        public static bool Matches(string normalizedInput, string keyword)
        {
            if (string.IsNullOrEmpty(normalizedInput) || string.IsNullOrEmpty(keyword)) return false;
            string k = Normalize(keyword);
            if (k.Length == 0) return false;
            if (k.IndexOf(' ') >= 0) return (" " + normalizedInput + " ").Contains(" " + k + " ") || normalizedInput.Contains(k);
            foreach (var word in normalizedInput.Split(' '))
            {
                if (word.Length == 0) continue;
                if (k.Length <= 3)
                {
                    if (word == k || word == k + "s") return true;
                }
                else if (word.Contains(k))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
