using System;

namespace SecondCursor.Core.Story
{
    /// <summary>What a Jotter line said about the player's name.</summary>
    public enum NameKind { None, Name, Forbidden }

    public readonly struct NameResult
    {
        public readonly NameKind Kind;
        /// <summary>The name as typed, lower case (only for <see cref="NameKind.Name"/>).</summary>
        public readonly string Name;
        /// <summary>Which forbidden name it was: "taken", "casey", "gary", "voss", "017" (only for <see cref="NameKind.Forbidden"/>).</summary>
        public readonly string ForbiddenKey;

        public NameResult(NameKind kind, string name, string forbiddenKey)
        {
            Kind = kind;
            Name = name ?? "";
            ForbiddenKey = forbiddenKey ?? "";
        }

        public static readonly NameResult None = new NameResult(NameKind.None, "", "");
    }

    /// <summary>
    /// Phase Q2 (board C V3, "tell it your name"): a name is taken only from explicit forms ("my name is X", "my names X",
    /// "call me X", "name is X", "im called X"; never "i am X", which would capture "scared"). X is the first word after the form:
    /// letters and digits, 2 to 12 characters, not a stop word, not blocked or personal (<see cref="EchoFilter"/>). The story's own
    /// names are answered first and never stored. Engine-free, so the rules are unit-tested.
    /// </summary>
    public static class NameCapture
    {
        public const int MinLength = 2, MaxLength = 12;

        /// <summary>The forms, on the normalized line (<see cref="DialogueEngine.Normalize"/>: lower case, no apostrophes).</summary>
        static readonly string[] Forms = { "my name is ", "my names ", "call me ", "im called ", "i am called ", "name is " };

        /// <summary>Words that follow a form without being a name ("my name is not...", "call me later").</summary>
        static readonly string[] Stop =
        {
            "not", "none", "nobody", "nothing", "no", "what", "it", "its", "a", "an", "the", "you", "your", "me", "my", "is", "was", "yes",
            "secret", "unknown", "idk", "null", "that", "this", "here", "there", "who", "why", "how", "when", "if", "later", "back", "maybe",
            "again", "please", "sometime", "tomorrow", "crazy", "stupid", "dead", "scared", "same", "irrelevant", "private", "hidden",
            "something", "someone", "anyone", "whatever", "nope", "ok", "okay", "and", "but", "or", "so", "just", "really", "still", "also",
            "at", "now", "on", "in", "up", "out", "soon", "anytime", "whenever", "anything", "never", "insane", "by", "for", "to", "when",
        };

        /// <summary>(names, key): the story's own names, answered in her voice and never stored.</summary>
        static readonly (string[] names, string key)[] Forbidden =
        {
            (new[] { "ellen", "marsh" }, "taken"),
            (new[] { "casey", "rourke", "crourke" }, "casey"),
            (new[] { "gary", "pruitt" }, "gary"),
            (new[] { "voss" }, "voss"),
            (new[] { "017", "17" }, "017"),
        };

        /// <summary>What the line says about a name (it is normalized here).</summary>
        public static NameResult Read(string line)
        {
            string norm = DialogueEngine.Normalize(line);
            if (norm.Length == 0) return NameResult.None;
            string padded = " " + norm + " ";
            foreach (var form in Forms)
            {
                int at = padded.IndexOf(" " + form, StringComparison.Ordinal);
                if (at < 0) continue;
                // Review Q2: a bare "name is" only opens the line ("the file name is employee" and "your name is ellen" are not yours).
                if (form == "name is " && at != 0) continue;
                string rest = padded.Substring(at + 1 + form.Length).Trim();
                if (rest.Length == 0) continue;
                int space = rest.IndexOf(' ');
                string word = space < 0 ? rest : rest.Substring(0, space);
                return Classify(word);
            }
            return NameResult.None;
        }

        /// <summary>One word as a name: forbidden, accepted (lower case) or nothing.</summary>
        public static NameResult Classify(string word)
        {
            word = (word ?? "").Trim().ToLowerInvariant();
            foreach (var (names, key) in Forbidden)
                if (Array.IndexOf(names, word) >= 0) return new NameResult(NameKind.Forbidden, "", key);
            return IsValid(word) ? new NameResult(NameKind.Name, word, "") : NameResult.None;
        }

        /// <summary>A name the game may keep and show: 2 to 12 letters or digits, not a stop word, not the story's, not blocked or personal.</summary>
        public static bool IsValid(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length < MinLength || name.Length > MaxLength) return false;
            foreach (char c in name)
                if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))) return false;
            if (Array.IndexOf(Stop, name) >= 0) return false;
            foreach (var (names, _) in Forbidden)
                if (Array.IndexOf(names, name) >= 0) return false;
            return !EchoFilter.Withheld(name);
        }
    }
}
