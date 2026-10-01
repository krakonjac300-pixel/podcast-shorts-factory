using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using SecondCursor.Core.Content;
using SecondCursor.Core.Game;
using SecondCursor.Core.Story;
using Xunit;

namespace SecondCursor.Tests
{
    /// <summary>
    /// Phase Q3 (voice and images): chat on every night, the six question groups, the fourth-wall groups and the slang, MOVE YOUR HAND and
    /// WHO ELSE IS WATCHING, the empty chair, the recovered page, the finale's staging and the line rewrites, Ruth in the Log Off log,
    /// the estate record, QUIET and the viewer's administrator, and gestures. Keyword order is tested because first match decides.
    /// </summary>
    public class PhaseQ3Tests
    {
        static readonly string Dir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../Assets/SecondCursor/Resources/Content"));
        static readonly JsonSerializerOptions Options = new JsonSerializerOptions { IncludeFields = true };
        static bool Present => File.Exists(Path.Combine(Dir, "strings.json"));

        static T Read<T>(string folder, string name) where T : class
        {
            string path = Path.Combine(Dir, folder, name + ".json");
            return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) : null;
        }

        static ContentPack Pack(string folder) => new ContentPack
        {
            Strings = Read<StringTableData>(folder, "strings"), Story = Read<StoryData>(folder, "story"),
            FileSystem = Read<FileSystemData>(folder, "filesystem"), Emails = Read<EmailsData>(folder, "emails"),
            Employees = Read<EmployeesData>(folder, "employees"), WorkOrders = Read<WorkOrdersData>(folder, "workorders"),
            Tasks = Read<TasksData>(folder, "tasks"), Dialogue = Read<DialogueData>(folder, "dialogue"),
        };

        static ContentDatabase Night(int night)
        {
            var pack = Pack("").Overlay(Pack("full"));
            for (int n = 2; n <= night; n++) pack = pack.Overlay(Pack("night" + n));
            return pack.Build();
        }

        static ContentDatabase Demo() => Pack("").Build();

        static DialogueReply Say(ContentDatabase db, string exchange, string said) => new DialogueEngine(db).Respond(db.Exchange(exchange), said);

        /// <summary>Every conversation with Ellen, by night (the confirm and the audience question are single-question exchanges).</summary>
        static readonly (int night, string id)[] Ellen =
        {
            (1, "ex_stop"), (1, "ex_two"), (1, "ex_three"), (2, "ex2_back"), (3, "ex3_ruth"), (3, "ex3_final"),
        };

        static readonly (int night, string id)[] Gary = { (2, "ex2_gary_one"), (2, "ex2_gary_two") };

        public static IEnumerable<object[]> EllenExchanges() => Ellen.Select(e => new object[] { e.night, e.id });
        public static IEnumerable<object[]> EveryExchange() => Ellen.Concat(Gary).Select(e => new object[] { e.night, e.id });

        // ------------------------------------------------------------------ chat on every night (V2)

        [Theory]
        [MemberData(nameof(EveryExchange))]
        public void ChatIsAnsweredInEveryExchange(int night, string id)
        {
            if (!Present) return;
            var db = Night(night);
            foreach (var said in new[] { "chat", "hi chat", "are you on twitch", "youtube", "my audience", "my followers", "the mods", "viewers", "subscribe" })
            {
                var r = Say(db, id, said);
                Assert.False(r.IsFallback, id + ": '" + said + "' falls back");
                Assert.Equal("chat", r.Tag);
            }
        }

        [Fact]
        public void ChatGetsItsOwnReplyOnNightsTwoAndThree()
        {
            if (!Present) return;
            Assert.Equal(new[] { "THEY CAME BACK TOO" }, Say(Night(2), "ex2_back", "say hi to chat").Lines);
            Assert.Equal(new[] { "tell them hi" }, Say(Night(2), "ex2_gary_one", "chat says hi").Lines.Skip(1).ToArray());
            Assert.Equal(new[] { "tell them it's late", "they should sleep" }, Say(Night(2), "ex2_gary_two", "chat is asking").Lines);
            Assert.Equal(new[] { "TELL THEM", "WHAT SHE DID" }, Say(Night(3), "ex3_ruth", "chat wants to know").Lines);
            var final = Say(Night(3), "ex3_final", "chat is watching");
            Assert.Equal(new[] { "LET THEM CHOOSE", "THEY WONT BE HERE AT SEVEN" }, final.Lines);
            // A chat group never picks an exit: the finale keeps listening for STAY, LET GO and the name.
            Assert.Equal("chat", final.Tag);
            Assert.Equal("stay", Say(Night(3), "ex3_final", "chat says stay").Tag);
            Assert.Equal("stay", Say(Night(3), "ex3_final", "i will stay").Tag);
        }

        [Fact]
        public void ChatMemoryIsRememberedAndPaidOff()
        {
            var flags = new NarrativeFlags();
            Assert.False(MemoryFlags.NamedChatAny(flags));
            flags.Set(MemoryFlags.N1NamedChat);
            Assert.True(MemoryFlags.NamedChatAny(flags));
            var other = new NarrativeFlags();
            other.Set(MemoryFlags.N2NamedChat);
            Assert.True(MemoryFlags.NamedChatAny(other));
            if (!Present) return;
            Assert.Equal(new[] { "ARE THEY STILL WATCHING", "TELL THEM HELLO" }, Night(2).Lines("n2_back_mem_chat"));
            Assert.Equal(new[] { "TELL THEM", "THIS IS THE LAST NIGHT" }, Night(3).Lines("n3_intro_mem_chat"));
            // KEEP: THEY CAN WATCH before YOU SAID, only when the player spoke to chat; the speakers stay one per line.
            var db = Night(3);
            foreach (var id in new[] { "n3_end_keep_stay", "n3_end_keep_default" })
            {
                var set = db.LineSet(id);
                Assert.Contains("{chatwatch}", set.lines);
                Assert.Equal(set.lines.Length, set.speakers.Length);
                var withChat = NightTemplates.FillLines(set.lines, NightTemplates.ForSave(flags, new SaveData { playerLines = new[] { "who are you" } }), set.speakers, out var sp1);
                Assert.Contains("THEY CAN WATCH", withChat);
                Assert.True(Array.IndexOf(withChat, "THEY CAN WATCH") < Array.IndexOf(withChat, "YOU SAID WHO ARE YOU"));
                Assert.Equal(withChat.Length, sp1.Length);
                Assert.Equal("WE WORK NIGHTS", withChat[withChat.Length - 1]);
                var without = NightTemplates.FillLines(set.lines, NightTemplates.ForSave(new NarrativeFlags(), new SaveData { playerLines = new[] { "who are you" } }), set.speakers, out var sp2);
                Assert.DoesNotContain("THEY CAN WATCH", without);
                Assert.Equal(without.Length, sp2.Length);
            }
        }

        // ------------------------------------------------------------------ the six questions (D2)

        [Theory]
        [MemberData(nameof(EllenExchanges))]
        public void TheQuestionsPlayersTypeAreAnswered(int night, string id)
        {
            if (!Present) return;
            var db = Night(night);
            string[][] probes =
            {
                new[] { "am i dead", "am i a ghost", "did i die", "am i the copy" },
                new[] { "how did you die", "who killed you", "what happened to you", "did you die", "he died here" },
                new[] { "what is custodial", "who is the janitor", "the figure", "is 000 real" },
                new[] { "where is denise", "denise?", "castell", "what about 175" },
                new[] { "who is lundy", "achterberg", "theodore", "marta" },
            };
            var replies = probes.Select(group => group.Select(said => Say(db, id, said)).ToArray()).ToArray();
            foreach (var group in replies)
            {
                foreach (var r in group)
                {
                    Assert.False(r.IsFallback, id + " falls back");
                    Assert.Equal(group[0].Lines, r.Lines);
                }
            }
            // Five different answers: nothing collapses into a catch-all group.
            Assert.Equal(5, replies.Select(g => string.Join("|", g[0].Lines)).Distinct().Count());
        }

        [Fact]
        public void TheDeathGroupDoesNotTakeOtherQuestions()
        {
            if (!Present) return;
            // Whole words: "studied" and "skilled" are not about her death, and "who killed gary" is still about Gary, "i killed it" still a delete.
            foreach (var (night, id) in Ellen)
            {
                var db = Night(night);
                var die = string.Join("|", Say(db, id, "how did you die").Lines);
                foreach (var said in new[] { "i studied it", "he is skilled", "who killed gary", "i killed it" })
                    Assert.NotEqual(die, string.Join("|", Say(db, id, said).Lines));
            }
            Assert.Equal(Say(Night(1), "ex_stop", "gary").Lines, Say(Night(1), "ex_stop", "who killed gary").Lines);
        }

        [Fact]
        public void TheAnswersAreThePlannedOnes()
        {
            if (!Present) return;
            Assert.Equal(new[] { "IT COMES AT THREE", "REMAIN SEATED" }, Say(Night(1), "ex_stop", "what is custodial").Lines);
            Assert.Equal(new[] { "THEY SAID IT WOULD FEEL", "LIKE FALLING ASLEEP" }, Say(Night(1), "ex_stop", "how did you die").Lines);
            Assert.Equal(new[] { "SHE DIDNT SIGN IT", "LOOK AT WHO DID" }, Say(Night(1), "ex_two", "denise").Lines);
            Assert.Equal(new[] { "DID YOU DRIVE IN" }, Say(Night(2), "ex2_back", "am i dead").Lines);
            Assert.Equal(new[] { "SHELF NINETEEN" }, Say(Night(3), "ex3_ruth", "where is denise").Lines);
            Assert.Equal(new[] { "IT DOES NOT FEEL", "LIKE FALLING ASLEEP" }, Say(Night(3), "ex3_final", "how did you die").Lines);
            Assert.Equal(new[] { "AT SEVEN IT COMES UP", "DONT WATCH IT COME" }, Say(Night(3), "ex3_final", "what is custodial").Lines);
            Assert.Equal(new[] { "it was very gentle", "that's the worst part" }, Say(Night(2), "ex2_gary_one", "what is custodial").Lines);
            Assert.Equal(new[] { "i didn't", "not all the way" }, Say(Night(2), "ex2_gary_one", "how did you die").Lines);
        }

        [Theory]
        [MemberData(nameof(EllenExchanges))]
        public void TheOldGroupsStillAnswerTheirOwnWords(int night, string id)
        {
            if (!Present) return;
            var db = Night(night);
            // "are you dead" is still about her (the REAL group), "kill it" is still about deleting, "i figured it out" is not the figure.
            string real = string.Join("|", Say(db, id, "are you real").Lines);
            Assert.Equal(real, string.Join("|", Say(db, id, "are you dead").Lines));
            Assert.NotEqual(string.Join("|", Say(db, id, "am i dead").Lines), real);
            string del = string.Join("|", Say(db, id, "delete it").Lines);
            Assert.Equal(del, string.Join("|", Say(db, id, "kill it").Lines));
            Assert.NotEqual(string.Join("|", Say(db, id, "the janitor").Lines), string.Join("|", Say(db, id, "i figured it out").Lines));
            Assert.NotEqual(string.Join("|", Say(db, id, "denise").Lines), string.Join("|", Say(db, id, "denim").Lines));
            // "i see" is still the look group, not the fourth wall.
            Assert.NotEqual(string.Join("|", Say(db, id, "can you see me").Lines), string.Join("|", Say(db, id, "i see it").Lines));
        }

        [Fact]
        public void HerNameOnNightOneIsAnsweredAndKept()
        {
            if (!Present) return;
            var db = Night(1);
            foreach (var id in new[] { "ex_stop", "ex_two", "ex_three" })
            {
                Assert.Equal("name", Say(db, id, "Ellen?").Tag);
                Assert.Equal("name", Say(db, id, "is your name ellen marsh").Tag);
                Assert.NotEqual("name", Say(db, id, "excellent").Tag);
                Assert.NotEqual("name", Say(db, id, "marshmallow").Tag);
            }
            Assert.Equal(new[] { "NOBODY HAS SAID THAT", "IN ELEVEN YEARS" }, Say(db, "ex_stop", "ellen").Lines);
            Assert.Equal(AchievementIds.HerName, AchievementRules.OnReply("", "name"));
            var flags = new NarrativeFlags();
            Assert.False(MemoryFlags.SaidNameAny(flags));
            flags.Set(MemoryFlags.N1SaidName);
            Assert.True(MemoryFlags.SaidNameAny(flags));
        }

        // ------------------------------------------------------------------ the fourth wall and the slang (V2)

        [Theory]
        [MemberData(nameof(EllenExchanges))]
        public void WebcamUninstallAndTheDateAreAnsweredInTheStory(int night, string id)
        {
            if (!Present) return;
            var db = Night(night);
            var see = new[] { "can you see me", "what is my address", "do you have my webcam", "where i live", "this is doxxing" };
            var quit = new[] { "i will uninstall this", "alt f4", "close the game", "i want a refund", "open task manager" };
            var year = new[] { "what year is it", "what day is it", "what is the date", "is it 1998" };
            foreach (var set in new[] { see, quit, year })
            {
                string expected = string.Join("|", Say(db, id, set[0]).Lines);
                foreach (var said in set)
                {
                    var r = Say(db, id, said);
                    Assert.False(r.IsFallback, id + ": '" + said + "' falls back");
                    Assert.Equal(expected, string.Join("|", r.Lines));
                    Assert.Equal("", r.Tag);
                }
            }
            Assert.Equal(new[] { "THE CLOCK SAYS 98", "I STOPPED AT 87" }, Say(db, id, "what year is it").Lines);
        }

        [Fact]
        public void TheFourthWallAnswersAreOneNightEach()
        {
            if (!Present) return;
            Assert.Equal(new[] { "I CANT SEE YOUR ROOM", "ONLY THIS ONE" }, Say(Night(1), "ex_stop", "can you see me").Lines);
            Assert.Equal(new[] { "NOT YOUR ROOM", "CAM 03 IS ENOUGH" }, Say(Night(2), "ex2_back", "can you see me").Lines);
            Assert.Equal(new[] { "I ONLY EVER SAW THIS DESK", "AND WHO SITS THERE" }, Say(Night(3), "ex3_ruth", "can you see me").Lines);
            Assert.Equal(new[] { "YOU CAN CLOSE IT", "I STAY OPEN" }, Say(Night(1), "ex_three", "uninstall").Lines);
            Assert.Equal(new[] { "YOU CLOSED IT LAST NIGHT", "HERE WE ARE" }, Say(Night(2), "ex2_back", "alt f4").Lines);
            Assert.Equal(new[] { "CLOSE IT THEN", "I KEPT A COPY" }, Say(Night(3), "ex3_final", "delete the game").Lines);
            // In the finale a streamer's "uninstall" is not a let-go: no bin mode comes of it.
            Assert.Equal("", Say(Night(3), "ex3_final", "delete the game").Tag);
            Assert.Equal("letgo", Say(Night(3), "ex3_final", "delete it").Tag);
        }

        [Theory]
        [MemberData(nameof(EllenExchanges))]
        public void TheTwitchSlangIsKnown(int night, string id)
        {
            if (!Present) return;
            var db = Night(night);
            string slang = string.Join("|", Say(db, id, "skibidi").Lines);
            foreach (var said in new[] { "pog", "poggers", "kekw", "lul", "omegalul", "monkas", "kappa", "sadge", "copium", "bruh", "sus" })
                Assert.Equal(slang, string.Join("|", Say(db, id, said).Lines));
            // The short ones are whole words: "suspicious" and "pogrom" are not slang.
            Assert.NotEqual(slang, string.Join("|", Say(db, id, "suspicious").Lines));
            Assert.NotEqual(slang, string.Join("|", Say(db, id, "pogrom").Lines));
        }

        // ------------------------------------------------------------------ keyword order and the finale

        [Theory]
        [MemberData(nameof(EllenExchanges))]
        public void TheGroupsKeepTheirOrder(int night, string id)
        {
            if (!Present) return;
            var ex = Night(night).Exchange(id);
            int At(string firstKeyword) => Array.FindIndex(ex.responses, r => r.keywords.Length > 0 && r.keywords[0] == firstKeyword);
            int chat = At("chat"), see = At("see me"), quit = At("uninstall"), year = At("what year"), self = At("am i dead"), die = At("=die"),
                custodial = At("custodial"), denise = At("denise"), before = At("lundy"), del = At("delete"), name = At("=ellen"), skibidi = At("skibidi");
            foreach (int i in new[] { chat, see, quit, year, self, die, custodial, denise, before, del, skibidi }) Assert.True(i >= 0, id);
            if (id == "ex3_final")
            {
                // STAY first, then chat and the fourth wall, then the exits, her name, the six, and only then the bin's own words.
                int stay = At("stay"), go = At("stop"), letgo = At("let you go");
                Assert.Equal(0, stay);
                Assert.True(stay < chat && chat < see && see < quit && quit < year && year < go && go < letgo && letgo < name && name < self, id);
            }
            else
            {
                int first = ex.responses[0].tag == DialogueEngine.NameTag ? 1 : 0;
                Assert.True(first <= chat && chat < see, id);
                Assert.True(see < quit && quit < year, id);
                // Night 1 asks her name after the fourth wall; Nights 2 and 3 already had it (right after chat), so the fourth wall follows it.
                if (night == 1) Assert.True(year < name && name < self, id);
                else Assert.True(chat < name && name < see && year < self, id);
            }
            Assert.True(self < die && die < custodial && custodial < denise && denise < before && before < del && before < skibidi, id);
        }

        [Fact]
        public void TheNameGroupsStayFirstAndTheFinaleHasNone()
        {
            if (!Present) return;
            foreach (var (night, id) in new[] { (1, "ex_stop"), (1, "ex_two"), (1, "ex_three"), (2, "ex2_back"), (2, "ex2_gary_one"), (3, "ex3_ruth") })
            {
                var ex = Night(night).Exchange(id);
                Assert.Equal(DialogueEngine.NameTag, ex.responses[0].tag);
                Assert.Empty(ex.responses[0].keywords);
                Assert.Equal(1, ex.responses.Count(r => r.tag == DialogueEngine.NameTag));
            }
            foreach (var (night, id) in new[] { (2, "ex2_gary_two"), (2, "ex2_audience"), (3, "ex3_final"), (3, "ex3_confirm") })
                Assert.Null(DialogueEngine.NameResponse(Night(night).Exchange(id)));
            // A name still beats every keyword: "my name is Kosta" is read as a name, whatever else the line says.
            var r = NameCapture.Read("my name is kosta, can you see me on twitch");
            Assert.Equal(NameKind.Name, r.Kind);
            Assert.Equal("kosta", r.Name);
            Assert.Equal(NameKind.Forbidden, NameCapture.Read("my name is ellen").Kind);
        }

        [Fact]
        public void TheFinalesStayAndLetGoRepliesSurvive()
        {
            if (!Present) return;
            var db = Night(3);
            foreach (var said in new[] { "stay", "i will stay", "yes", "chat says stay", "dont go", "i wont let you go" })
                Assert.Equal("stay", Say(db, "ex3_final", said).Tag);
            foreach (var said in new[] { "let you go", "goodbye", "put you in the bin", "delete it" })
            {
                var r = Say(db, "ex3_final", said);
                Assert.Equal("letgo", r.Tag);
                // D5.4: the price is half known before it is paid.
                Assert.Contains("SOMEONE ALWAYS SITS HERE", r.Lines);
                Assert.Contains("HOLD ON WHEN I PULL", r.Lines);
            }
            Assert.Equal("go", Say(db, "ex3_final", "let me go").Tag);
            Assert.Equal("name", Say(db, "ex3_final", "ellen").Tag);
            Assert.Equal("confirm", Say(db, "ex3_confirm", "yes").Tag);
            Assert.Equal("cancel", Say(db, "ex3_confirm", "no").Tag);
        }

        // ------------------------------------------------------------------ the echo filter still guards every echo

        [Fact]
        public void NewRepliesNeverEchoThePlayer()
        {
            if (!Present) return;
            var tokenRegex = new Regex(@"\{([^}]*)\}");
            foreach (var (night, id) in Ellen.Concat(Gary).Concat(new[] { (2, "ex2_audience") }))
            {
                var ex = Night(night).Exchange(id);
                foreach (var r in ex.responses.Where(r => r.tag != DialogueEngine.NameTag))
                    foreach (var line in r.reply)
                        Assert.Empty(tokenRegex.Matches(line).Cast<Match>().Select(m => m.Value));
            }
        }

        // ------------------------------------------------------------------ voice rules and the demo

        static IEnumerable<(string where, string line)> ReplyLines(ContentDatabase db, string id)
        {
            var ex = db.Exchange(id);
            foreach (var l in ex.entityLines) yield return (id, l);
            foreach (var l in ex.fallback) yield return (id + ".fallback", l);
            foreach (var l in ex.silence) yield return (id + ".silence", l);
            foreach (var r in ex.responses)
                foreach (var l in r.reply) yield return (id + ".reply", l);
        }

        [Fact]
        public void EveryNewLineKeepsItsVoice()
        {
            if (!Present) return;
            var ellen = new Regex("^[A-Z0-9 ]+$");
            var bad = new List<string>();
            void Check(string where, string line, bool gary)
            {
                string l = Regex.Replace(line, @"\{[^}]*\}", gary ? "x" : "X");
                int words = l.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length;
                if (words < 1 || words > 6) bad.Add(where + ": " + words + " words: " + line);
                if (gary ? l.Any(char.IsUpper) : !ellen.IsMatch(l)) bad.Add(where + ": " + line);
            }
            foreach (var (night, id) in Ellen.Concat(new[] { (2, "ex2_audience") }))
                foreach (var (where, line) in ReplyLines(Night(night), id)) Check(where, line, false);
            foreach (var (night, id) in Gary)
                foreach (var (where, line) in ReplyLines(Night(night), id)) Check(where, line, true);
            foreach (var id in new[] { "n1_move_hand", "n2_empty_chair", "n2_back_mem_chat", "n3_intro_mem_chat", "n3_recovered", "n3_tug_hold1" })
                foreach (var line in Night(3).LineSet(id).lines) Check(id, line, false);
            Assert.True(bad.Count == 0, string.Join("\n", bad));
        }

        [Fact]
        public void TheDemoGetsNoNightTwoOrThreeText()
        {
            if (!Present) return;
            var demo = Demo();
            string[] banned = { "ELLEN", "MARSH", "LUNDY", "ACHTERBERG", "SHELF", "ROUNDS", "TOMORROW", "NIGHT TWO", "SEVEN IT COMES", "SOMEONE ALWAYS SITS" };
            foreach (var id in new[] { "ex_stop", "ex_two", "ex_three" })
                foreach (var (where, line) in ReplyLines(demo, id))
                    foreach (var b in banned) Assert.False(line.Contains(b), where + " leaks '" + b + "': " + line);
            // The night 2 and 3 exchanges, their line sets and files do not exist in the demo.
            foreach (var id in new[] { "ex2_audience", "ex2_back", "ex3_final" }) Assert.Null(demo.Exchange(id));
            foreach (var id in new[] { "n2_empty_chair", "n2_back_mem_chat", "n3_intro_mem_chat", "n3_recovered", "n3_end_logoff_ruth_out", "n3_tug_hold1" })
                Assert.Empty(demo.Lines(id));
            Assert.DoesNotContain(demo.FileSystem.files, f => f.id == ContentIds.FileRecovered);
            // MOVE YOUR HAND is the demo's (Night 1's reveal); QUIET is taught in its FAQ.
            Assert.Equal(new[] { "MOVE YOUR HAND" }, demo.Lines("n1_move_hand"));
            Assert.Equal(new[] { "THEY WATCH YOU WORK", "THEY WATCHED ME TOO", "I CAN OPEN IT", "LOOK AT YOU" }, demo.Dialogue.cameraLines);
            string faq = demo.Email("mail_retention_faq").body;
            Assert.Contains("Flag it QUIET. Most records are quiet.", faq);
            Assert.Contains("Clear the flag. File it with Retention.", faq);
            Assert.True(faq.IndexOf("Q: May I open them?", StringComparison.Ordinal) < faq.IndexOf("Q: A record did not respond", StringComparison.Ordinal));
            Assert.True(faq.IndexOf("Q: A QUIET record", StringComparison.Ordinal) < faq.IndexOf("Q: A file will not shred.", StringComparison.Ordinal));
        }

        // ------------------------------------------------------------------ T7: who else is watching

        [Fact]
        public void WhoElseIsWatchingHasAnAnswerForChatFamilyAndNobody()
        {
            if (!Present) return;
            var db = Night(2);
            var ex = db.Exchange("ex2_audience");
            Assert.Equal(new[] { "WHO ELSE IS WATCHING" }, ex.entityLines);
            foreach (var said in new[] { "chat", "twitch", "my viewers", "youtube", "friends", "everyone" })
                Assert.Equal(new[] { "TELL THEM NOT TO LOOK", "IT MOVES WHEN THEY WATCH TOO" }, Say(db, "ex2_audience", said).Lines);
            foreach (var said in new[] { "nobody", "no one", "im alone", "just me", "only me", "by myself", "no", "nope" })
            {
                var r = Say(db, "ex2_audience", said);
                Assert.Equal(new[] { "THEN WHO IS BREATHING" }, r.Lines);
                Assert.Equal("breath", r.Tag);
            }
            foreach (var said in new[] { "my mom", "dad is here", "my brother", "my sister", "roommate", "girlfriend", "boyfriend", "my wife", "husband" })
                Assert.Equal(new[] { "DONT LET THEM SIT HERE" }, Say(db, "ex2_audience", said).Lines);
            foreach (var said in new[] { "you", "017" })
                Assert.Equal(new[] { "ONLY ME" }, Say(db, "ex2_audience", said).Lines);
            var fallback = Say(db, "ex2_audience", "banana");
            Assert.True(fallback.IsFallback);
            Assert.Equal(new[] { "GOOD", "KEEP IT THAT WAY" }, fallback.Lines);
            Assert.Equal("", ex.next);
            // A chat answer is a chat memory too (Night 3 greets the viewers).
            Assert.Equal("chat", Say(db, "ex2_audience", "chat").Tag);
        }

        // ------------------------------------------------------------------ D5: the line rewrites

        [Fact]
        public void TheSixLinesAreRewritten()
        {
            if (!Present) return;
            var db = Night(3);
            Assert.Equal(new[] { "TWENTY SEVEN YEARS", "SAME FOLDERS" }, Night(2).Lines("n2_help_more"));
            Assert.Equal(new[] { "HE IS FINISHED", "THEY HAVE ALL OF HIM", "TOMORROW THEY WANT YOU" }, db.Lines("n2_end_finished"));
            Assert.Equal(new[] { "HE STAYS", "HALF IS ENOUGH", "FOR NOW", "TOMORROW THEY WANT YOU" }, db.Lines("n2_end_kept"));
            Assert.Equal(new[] { "KEEP WORKING", "THEY COUNT THE PAUSES" }, Say(Night(2), "ex2_back", "banana").Lines);
            Assert.Equal(new[] { "sorry about the mess", "i was going to say", "don't fight her" }, Say(Night(2), "ex2_gary_one", "your notes").Lines);
            Assert.Equal("Custodial is on this camera. Custodial comes to operators who watch it. Switch to another camera.", db.Text("rounds.onit"));
            // SEE YOU TOMORROW NIGHT is Night 1's cliffhanger and LOG OFF's sting only.
            Assert.DoesNotContain("SEE YOU TOMORROW NIGHT", db.Lines("n2_end_finished"));
            Assert.Contains("SEE YOU TOMORROW NIGHT", db.Lines("n3_end_logoff"));
        }

        // ------------------------------------------------------------------ D1, D3, D4, D6, D7, D10

        [Fact]
        public void TheEmptyChairIsOneLine()
        {
            if (!Present) return;
            Assert.Equal(new[] { "LOOK AT YOU" }, Night(2).Lines("n2_empty_chair"));
            Assert.Equal("LOOK AT YOU", Demo().Dialogue.cameraLines.Last());
        }

        [Fact]
        public void TheRecoveredPageIsHiddenOnTheDesktopAndSaysWhatTheCopyTypes()
        {
            if (!Present) return;
            var db = Night(3);
            var page = db.FileSystem.files.First(f => f.id == ContentIds.FileRecovered);
            Assert.Equal("desktop", page.folder);
            Assert.True(page.hidden);
            Assert.Equal("recovered_0558.txt", page.name);
            Assert.Contains("is this on", page.content);
            Assert.Contains("my name is c\ni had it a minute ago", page.content);
            Assert.Contains("[NOT SAVED]", page.content);
            // Casey's voice is lowercase; the page is the held and the living before the seat takes them.
            foreach (var line in page.content.Split('\n').Skip(3).TakeWhile(l => !l.StartsWith("---")))
                Assert.Equal(line.ToLowerInvariant(), line);
            string notice = db.Text("lost.recovered");
            Assert.Contains("recovered 1 unsaved Jotter page", notice);
            Assert.Contains("\"is this on\"", notice);
            Assert.Equal(new[] { "I TYPED THAT TOO" }, db.Lines("n3_recovered"));
            Assert.DoesNotContain(Night(2).FileSystem.files, f => f.id == ContentIds.FileRecovered);
        }

        [Fact]
        public void TheFinaleLetsHerLoosenHerHand()
        {
            if (!Present) return;
            var db = Night(3);
            Assert.Equal(new[] { "YOU WILL HAVE TO TAKE IT" }, db.Lines("n3_tug_hold1"));
            // The loosening after the second win and the hold's own lines are the Phase P ones, untouched.
            Assert.Equal(new[] { "I CANT STOP MY HAND" }, db.Lines("n3_tug_giveup"));
            Assert.Equal(new[] { "I CANT STOP MY HAND", "HOLD ON", "DONT LET GO" }, db.Lines("n3_tug_letgo"));
        }

        [Fact]
        public void RuthReachesTheLogOff()
        {
            if (!Present) return;
            var db = Night(3);
            var sys = db.Lines("n3_end_logoff_sys").ToList();
            int lobby = sys.FindIndex(l => l.StartsWith("LOBBY EXIT", StringComparison.Ordinal));
            Assert.True(lobby >= 0);
            Assert.Equal(new[] { "LOBBY EXIT ... 07:02 (118)" }, db.Lines("n3_end_logoff_ruth_out"));
            Assert.Equal(new[] { "SEAT A-2 ... OCCUPIED (118)" }, db.Lines("n3_end_logoff_ruth_kept"));
            Assert.Equal("system", db.LineSet("n3_end_logoff_ruth_out").voice);
            Assert.Equal("system", db.LineSet("n3_end_logoff_ruth_kept").voice);
            // The decision is read the way Night 3 stores it.
            var flags = new NarrativeFlags();
            Assert.Null(WorkOrderRules.Remembered(flags, 3, ContentIds.Order3333));
            flags.Set(WorkOrderRules.MemoryKey(3, ContentIds.Order3333, "approve"));
            Assert.Equal("approve", WorkOrderRules.Remembered(flags, 3, ContentIds.Order3333));
        }

        [Fact]
        public void TheEstateRecordTiesTheBusinessToThePlayer()
        {
            if (!Present) return;
            string content = Night(3).FileSystem.files.First(f => f.id == "batch48_b").content;
            Assert.Contains("Class:     Estate records (Continuity)", content);
            Assert.Contains("1 new Continuity account opened\n11/19/98 (pre-need): ROURKE, C. (214).", content);
            Assert.Contains("Billed monthly. First payment 12/01/98.", content);
            Assert.Contains("Checked by: V. Pell 11/19/98 (covering)", content);
        }

        [Fact]
        public void WhoOpensTheViewerIsAClueChain()
        {
            if (!Present) return;
            var db = Night(3);
            Assert.Contains("LAST_OPENED_BY=ADMIN 1 (031)\nRECORD=ALL", db.FileSystem.files.First(f => f.id == "camview_cfg").content);
            var brandt = db.Employee("088");
            Assert.Equal("11/02/98 6:02 PM", brandt.lastLogin);
            Assert.Contains("Not at desk since 11/02/98.", brandt.notes);
            // Nights 1 and 2 are untouched.
            Assert.Equal("11/17/98 6:02 PM", Night(1).Employee("088").lastLogin);
            Assert.Equal("11/17/98 6:02 PM", Night(2).Employee("088").lastLogin);
            Assert.DoesNotContain("LAST_OPENED_BY", Night(2).FileSystem.files.First(f => f.id == "camview_cfg").content);
        }

        // ------------------------------------------------------------------ T9: gestures

        [Fact]
        public void GesturesBelongToEllensGroups()
        {
            if (!Present) return;
            foreach (var (night, id) in Ellen)
            {
                var db = Night(night);
                Assert.Equal("nod", Say(db, id, "yes").Gesture);
                Assert.Equal("shake", Say(db, id, "make me").Gesture);
                Assert.Equal("tremble", Say(db, id, "how did you die").Gesture);
                Assert.Equal(night == 1 ? "point" : "", Say(db, id, "show me the camera").Gesture);
                Assert.Equal("", Say(db, id, "banana").Gesture);
                Assert.Equal("", Say(db, id, "what is custodial").Gesture);
            }
            // Gary and the single-question exchanges never gesture.
            foreach (var (night, id) in Gary.Concat(new[] { (2, "ex2_audience"), (3, "ex3_confirm") }))
                foreach (var r in Night(night).Exchange(id).responses) Assert.True(string.IsNullOrEmpty(r.gesture), id);
        }

        // ------------------------------------------------------------------ the content as a whole

        [Fact]
        public void EveryNewTokenIsKnownAndFilesStayWithinTheirRules()
        {
            if (!Present) return;
            Assert.Contains("chatwatch", NightTemplates.Known);
            var tokens = NightTemplates.ForSave(new NarrativeFlags(), new SaveData());
            Assert.Equal("", tokens["chatwatch"]);
            var withChat = new NarrativeFlags();
            withChat.Set(MemoryFlags.N2NamedChat);
            Assert.Equal("THEY CAN WATCH", NightTemplates.ForSave(withChat, new SaveData())["chatwatch"]);
        }
    }
}
