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
        /// <summary>Story tag of the matched response (content "tag", e.g. "stay"); empty if none or fallback.</summary>
        public string Tag = "";
        /// <summary>Phase Q3 (T9): the gesture of the matched response ("" = none).</summary>
        public string Gesture = "";
    }

    /// <summary>
    /// Keyword-driven replies for the Notepad conversation. No AI: the player's free text is normalized and
    /// matched against authored keyword lists. Matching rules (to avoid "hi" matching "this"):
    ///  - keywords containing a space: substring match on the normalized sentence;
    ///  - short single words (&lt;= 3 letters): must equal a whole word (or the word + "s");
    ///  - longer single words: match any word containing them ("fuck" matches "fucking");
    ///  - a keyword starting with "=" matches whole words only ("=ellen" never matches "excellent").
    /// </summary>
    public sealed class DialogueEngine
    {
        readonly ContentDatabase _content;

        static readonly string[][] Categories =
        {
            new[] { "swear", "fuck", "shit", "wtf", "damn", "=hell", "bitch", "crap" },
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
            if (exchange == null || exchange.responses == null) return reply;
            bool finalChoice = exchange.id == "ex3_final" || exchange.id == "ex3_confirm";
            bool explicitStay = !finalChoice || IsExplicitStay(norm, exchange.id == "ex3_confirm");

            for (int i = 0; i < exchange.responses.Length; i++)
            {
                var r = exchange.responses[i];
                // Phase Q2: the name group answers only a captured name (NameReply), never a keyword.
                if (r == null || r.tag == NameTag || r.keywords == null) continue;
                // A word inside a refusal or a question cannot choose an ending.
                if (!explicitStay && (r.tag == "stay" || r.tag == "confirm")) continue;
                foreach (var kw in r.keywords)
                {
                    if (Matches(norm, kw))
                    {
                        reply.Lines = r.reply;
                        reply.MatchedKeyword = kw;
                        reply.ResponseIndex = i;
                        reply.Tag = r.tag ?? "";
                        // Record the accepted finale intention, rather than an unrelated generic keyword such as "wont".
                        if (reply.Tag == "stay" || reply.Tag == "letgo") reply.Category = reply.Tag;
                        else if (reply.Tag == "trust") reply.Category = "agree";
                        else if (reply.Tag == "distrust") reply.Category = "refuse";
                        reply.Gesture = r.gesture ?? "";
                        return reply;
                    }
                }
            }
            reply.Lines = exchange.fallback;
            reply.IsFallback = true;
            return reply;
        }

        /// <summary>Phase Q2 (V3): the tag of the response group that answers a name the player gave.</summary>
        public const string NameTag = "myname";

        /// <summary>The exchange's name group (its reply lines carry {NAME} or {name}), or null if it has none.</summary>
        public static ResponseData NameResponse(ExchangeData exchange)
        {
            if (exchange?.responses == null) return null;
            foreach (var r in exchange.responses)
                if (r != null && r.tag == NameTag) return r;
            return null;
        }

        // This high-stakes choice accepts a short, explicit statement. Uncertain free text still
        // receives an authored reply, but never commits the stay branch.
        static bool IsExplicitStay(string normalized, bool confirming)
        {
            foreach (string phrase in new[]
            {
                "stay", "yes stay", "i want to stay", "yes i want to stay", "i will stay", "ill stay",
                "id like to stay", "i would like to stay", "i choose to stay", "yes i will stay", "yes ill stay", "i want to stay here",
                "i am staying", "im staying", "stay with me", "i will stay with you", "i want to stay with you",
                "together", "with you", "keep me", "keep you", "remain", "not leaving", "im not leaving",
                "i am not leaving", "wont leave", "i wont leave", "dont go", "do not go", "dont leave", "do not leave"
            }) if (normalized == phrase) return true;
            if (confirming)
                foreach (string phrase in new[] { "yes", "yeah", "yep", "sure", "fine", "okay", "alright", "agree", "deal", "understood", "got it", "ok" })
                    if (normalized == phrase) return true;
            return false;
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
                if (c == '\'' || c == '`' || c == '\u2019' || c == '\u2018') continue;
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
            // "=word": whole words only ("=ellen" matches "Ellen?" but never "excellent").
            bool whole = keyword[0] == '=';
            string k = Normalize(whole ? keyword.Substring(1) : keyword);
            if (k.Length == 0) return false;
            if (whole) return (" " + normalizedInput + " ").Contains(" " + k + " ");
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
