using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using SecondCursor.Core.Audio;
using Xunit;

namespace SecondCursor.Tests
{
    /// <summary>
    /// Phase Q4 (CH9): a fingerprint of every sound in the bank, sample for sample. The bank's source was split into one file per family; this
    /// test proves nothing it makes changed (the SHA-256 is over the exact bits of every float of every seed listed), and it fails when a
    /// change to a shared DSP helper changes any sound, which nothing noticed before.
    /// </summary>
    public class SoundBankFingerprintTests
    {
        /// <summary>The seeds each sound is fingerprinted at: 0 and 1 (the preview and the game's), and the four a keystroke or a click uses.</summary>
        static readonly int[] Seeds = { 0, 1, 2, 3, 4 };

        static string Fingerprint(string id, int seed)
        {
            var samples = ProceduralSoundBank.Generate(id, seed);
            var bytes = new byte[samples.Length * sizeof(float)];
            Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(bytes);
                return samples.Length + ":" + string.Concat(hash.Take(12).Select(b => b.ToString("x2")));
            }
        }

        static IEnumerable<(string key, string print)> Everything()
        {
            foreach (var id in ProceduralSoundBank.Ids)
                foreach (var seed in Seeds)
                    yield return (id + "@" + seed, Fingerprint(id, seed));
        }

        [Fact]
        public void EverySoundIsSampleForSampleWhatItWas()
        {
            // Set SC_PRINT_FINGERPRINTS to a file path to (re)write the expected table from the current bank.
            string write = Environment.GetEnvironmentVariable("SC_PRINT_FINGERPRINTS");
            if (!string.IsNullOrEmpty(write))
            {
                File.WriteAllLines(write, Everything().Select(e => "            [\"" + e.key + "\"] = \"" + e.print + "\","));
                return;
            }
            Assert.Equal(50, ProceduralSoundBank.Ids.Count);
            Assert.Equal(ProceduralSoundBank.Ids.Count * Seeds.Length, Expected.Count);
            var wrong = new List<string>();
            foreach (var (key, print) in Everything())
                if (!Expected.TryGetValue(key, out var want) || want != print) wrong.Add(key + " is " + print + (Expected.TryGetValue(key, out want) ? ", was " + want : ", not in the table"));
            Assert.True(wrong.Count == 0, string.Join("\n", wrong));
        }

        [Fact]
        public void EverySoundIsStillRegisteredOnce()
        {
            Assert.Equal(ProceduralSoundBank.Ids.Count, ProceduralSoundBank.Ids.Distinct().Count());
            foreach (var id in ProceduralSoundBank.Ids) Assert.True(ProceduralSoundBank.Has(id), id);
        }

        // The table is generated from the unsplit bank (SC_PRINT_FINGERPRINTS) and never edited by hand.
        static readonly Dictionary<string, string> Expected = new Dictionary<string, string>
        {
            ["ui_click@0"] = "1499:584b0accff6377a9b8b29450",
            ["ui_click@1"] = "1499:27d7ba879a1df316448f50db",
            ["ui_click@2"] = "1499:ff3872755e94f482f6663587",
            ["ui_click@3"] = "1499:40d335d9057cf7c5b1370403",
            ["ui_click@4"] = "1499:002253aa96a6b67ab680b47d",
            ["ui_select@0"] = "706:56a5c2b4ef91cfdc4ffdfe03",
            ["ui_select@1"] = "706:8fdba87fd69d33ac5d7edc4b",
            ["ui_select@2"] = "706:cab7b1f2bf4670deea0a0685",
            ["ui_select@3"] = "706:782e103403a3d4c39e229e41",
            ["ui_select@4"] = "706:e8cc926e4a7f4e71e7106c24",
            ["ui_window@0"] = "4189:674d7e8add1bfefc19fd3788",
            ["ui_window@1"] = "4189:52a1717dcae6362ccac09409",
            ["ui_window@2"] = "4189:7058a35bbd9f2ed71b1a05f4",
            ["ui_window@3"] = "4189:f051f0c6fedb8437c7e4ed97",
            ["ui_window@4"] = "4189:d947b7c95320351b93903d40",
            ["notify_mail@0"] = "29988:d3bddc94ec791a692ac084dc",
            ["notify_mail@1"] = "29988:d3bddc94ec791a692ac084dc",
            ["notify_mail@2"] = "29988:d3bddc94ec791a692ac084dc",
            ["notify_mail@3"] = "29988:d3bddc94ec791a692ac084dc",
            ["notify_mail@4"] = "29988:d3bddc94ec791a692ac084dc",
            ["notify_task@0"] = "24255:2f45424f1be9654705aa78ff",
            ["notify_task@1"] = "24255:2f45424f1be9654705aa78ff",
            ["notify_task@2"] = "24255:2f45424f1be9654705aa78ff",
            ["notify_task@3"] = "24255:2f45424f1be9654705aa78ff",
            ["notify_task@4"] = "24255:2f45424f1be9654705aa78ff",
            ["sys_error@0"] = "18522:282d70cf91b55f4b8523d871",
            ["sys_error@1"] = "18522:282d70cf91b55f4b8523d871",
            ["sys_error@2"] = "18522:282d70cf91b55f4b8523d871",
            ["sys_error@3"] = "18522:282d70cf91b55f4b8523d871",
            ["sys_error@4"] = "18522:282d70cf91b55f4b8523d871",
            ["sys_warning@0"] = "25578:2a2f9853d690f81593792030",
            ["sys_warning@1"] = "25578:2a2f9853d690f81593792030",
            ["sys_warning@2"] = "25578:2a2f9853d690f81593792030",
            ["sys_warning@3"] = "25578:2a2f9853d690f81593792030",
            ["sys_warning@4"] = "25578:2a2f9853d690f81593792030",
            ["sys_startup@0"] = "194040:9c40c7f8dbd158702fc06ef4",
            ["sys_startup@1"] = "194040:728d8f385162636a4fb0d6c2",
            ["sys_startup@2"] = "194040:ec92d4ce0193ddc7d1f91c0c",
            ["sys_startup@3"] = "194040:88831bd0e10455c422916b5b",
            ["sys_startup@4"] = "194040:0e974d5d796615bb78e5a4a1",
            ["mouse_click@0"] = "4851:744aa8ae31c915b5ea1af109",
            ["mouse_click@1"] = "4851:300bbff3ad5d62eb0cf0fc31",
            ["mouse_click@2"] = "4851:07ca88566d36c456218df270",
            ["mouse_click@3"] = "4851:a47e8e850bfc4d406af7867d",
            ["mouse_click@4"] = "4851:2b735803b03508a79e8d2367",
            ["mouse_release@0"] = "3969:0caf121d8822c4fb34977bb9",
            ["mouse_release@1"] = "3969:0fe26b3ea4825cea453e3c51",
            ["mouse_release@2"] = "3969:fffdebd70651939b13a6ad07",
            ["mouse_release@3"] = "3969:18a09f10a33ac93dab26ee14",
            ["mouse_release@4"] = "3969:df747df608f66dfbef2d42a4",
            ["key_tap@0"] = "3087:801058ecdf3b3d1cf98e8922",
            ["key_tap@1"] = "3087:fb37ba651c0335dcb0f8fb21",
            ["key_tap@2"] = "3087:28717c9486edf41d69f10b62",
            ["key_tap@3"] = "3087:7f06c932cd4214ed17bcdb23",
            ["key_tap@4"] = "3087:8c204d7e088ae69d9c992205",
            ["key_space@0"] = "5072:448e134818def052fd8653ce",
            ["key_space@1"] = "5072:e0f9a413198e963e8df746fd",
            ["key_space@2"] = "5072:debbf1b3c09abf7bf3cacf19",
            ["key_space@3"] = "5072:40f94c875931e37219c03500",
            ["key_space@4"] = "5072:53d62481b8d4dc3281c5ba67",
            ["key_enter@0"] = "4851:0cf12a190991cb5f289187d0",
            ["key_enter@1"] = "4851:d0901821cc1f2f8b4fd826b7",
            ["key_enter@2"] = "4851:8e7cc0ae57757e571c97a1fe",
            ["key_enter@3"] = "4851:e216000d28b15fcdaa69ed4e",
            ["key_enter@4"] = "4851:26239a7963103512eb1845f2",
            ["bios_beep@0"] = "7497:9666edaae0ac547785fc92a3",
            ["bios_beep@1"] = "7497:9666edaae0ac547785fc92a3",
            ["bios_beep@2"] = "7497:9666edaae0ac547785fc92a3",
            ["bios_beep@3"] = "7497:9666edaae0ac547785fc92a3",
            ["bios_beep@4"] = "7497:9666edaae0ac547785fc92a3",
            ["hdd_seek@0"] = "18522:79c083a78acfa350591fae3d",
            ["hdd_seek@1"] = "18522:88eb1ae42da2583e0d40f2de",
            ["hdd_seek@2"] = "18522:eedeeead03558cdc6dba7712",
            ["hdd_seek@3"] = "18522:395067dc0ce05345ebae2b53",
            ["hdd_seek@4"] = "18522:89414019e95ababfc97090bf",
            ["hdd_spinup@0"] = "92610:e929fc456b522c7c33cff709",
            ["hdd_spinup@1"] = "92610:c0b8aaf2f5624e5315bbc184",
            ["hdd_spinup@2"] = "92610:b1f98033284c5f8ca803538d",
            ["hdd_spinup@3"] = "92610:df4c693a1de8d0c7cc71b0ba",
            ["hdd_spinup@4"] = "92610:efd4e1d42d1aa120fedd2c3b",
            ["crt_on@0"] = "55125:154cf227d67113530adb3338",
            ["crt_on@1"] = "55125:c8b52e3663bcd05c8e10cd32",
            ["crt_on@2"] = "55125:f99057a37f4f81ea6acdeaea",
            ["crt_on@3"] = "55125:01a19276e6a4ca46dbf7e9fd",
            ["crt_on@4"] = "55125:54e7bda03bae8368953ee252",
            ["crt_off@0"] = "66150:bccff72458f607ed08644af8",
            ["crt_off@1"] = "66150:3f78f31f45ac20f16bd2397a",
            ["crt_off@2"] = "66150:3a332876d79d8813ab574c1d",
            ["crt_off@3"] = "66150:122a187aed555992f1eb1b8a",
            ["crt_off@4"] = "66150:0bb7cf66d0e3e655e82ea7f3",
            ["shred_loop@0"] = "44100:947a2ee1aebda3c637041937",
            ["shred_loop@1"] = "44100:38dcae3a2a1b6ef1d6a07e7f",
            ["shred_loop@2"] = "44100:8493e4a1f6f0891a9118deda",
            ["shred_loop@3"] = "44100:4902c316905ce869b438c006",
            ["shred_loop@4"] = "44100:461ee8ceb6b6d9cd30d288a7",
            ["amb_room@0"] = "352800:894e8e21af4ac07ee0d9bc86",
            ["amb_room@1"] = "352800:72463dadea45a69983870119",
            ["amb_room@2"] = "352800:b7896b4d40e6567459db2cd9",
            ["amb_room@3"] = "352800:91919dae46c98aa1f5297f8e",
            ["amb_room@4"] = "352800:ed7177389d86a70d2e12a791",
            ["amb_fluorescent@0"] = "176400:c7b52c366137ad0751d72f34",
            ["amb_fluorescent@1"] = "176400:34ab7e32ae3e2d60cf9141bd",
            ["amb_fluorescent@2"] = "176400:08573166591f39caca2a5984",
            ["amb_fluorescent@3"] = "176400:0f7ca25491e242f441d7456f",
            ["amb_fluorescent@4"] = "176400:fbc53b7fe59c0212895a5e14",
            ["amb_crt_hum@0"] = "132300:dbfcc4c129a2ca22ab5b107c",
            ["amb_crt_hum@1"] = "132300:21e7a3a899cc394f8a26166e",
            ["amb_crt_hum@2"] = "132300:af58c0b8d94d7eddac0a1556",
            ["amb_crt_hum@3"] = "132300:2ceda9db394930858b71017d",
            ["amb_crt_hum@4"] = "132300:197f24e318050432327318de",
            ["drone_tension@0"] = "352800:db5da5b4b4dc5adfb3f53429",
            ["drone_tension@1"] = "352800:1bd8e168fbfe8e415715fcbb",
            ["drone_tension@2"] = "352800:551f21a8ff817f05e1807c95",
            ["drone_tension@3"] = "352800:883d89499ed6c3bc30b1d4dc",
            ["drone_tension@4"] = "352800:4ba51e01e1c2c13020afcb33",
            ["entity_static@0"] = "88200:10411385a3dc9b1491fc13c0",
            ["entity_static@1"] = "88200:d32be4e61ee6be59ec620c6a",
            ["entity_static@2"] = "88200:926eb03020749593f4773465",
            ["entity_static@3"] = "88200:aaafa63aa9f9ea7ed2e5e4dd",
            ["entity_static@4"] = "88200:2b01d5ca087226051bec35da",
            ["entity_appear@0"] = "44100:a88d466debb1edf04adb828f",
            ["entity_appear@1"] = "44100:128f053d34dd6d2a178d11d0",
            ["entity_appear@2"] = "44100:28d1341fbafe1157081843b5",
            ["entity_appear@3"] = "44100:a72132b7bac99014a41c2d57",
            ["entity_appear@4"] = "44100:61648dc3727e1ddb00ed9589",
            ["glitch_burst@0"] = "14112:018014e5d87fda9f91a65b09",
            ["glitch_burst@1"] = "14112:32e406d0af921eb9d07fd90e",
            ["glitch_burst@2"] = "14112:f2360d1e8dcdcdacc5b2cbae",
            ["glitch_burst@3"] = "14112:61e1cfdfbae51c7e1d6c1052",
            ["glitch_burst@4"] = "14112:ec0daaa77303d0630c5c3ba0",
            ["tug_strain@0"] = "66150:b0de0d8c9935468284fe96e0",
            ["tug_strain@1"] = "66150:9b57f8b646ae17bf97236749",
            ["tug_strain@2"] = "66150:0cbab97f944c7019d08dc1d6",
            ["tug_strain@3"] = "66150:b04fd8969301122615568c35",
            ["tug_strain@4"] = "66150:a0fda492379f58d77af9dcc6",
            ["grab_snap@0"] = "9702:39b911113da676b23c8f2f33",
            ["grab_snap@1"] = "9702:c673e8a658b446d7939ac16d",
            ["grab_snap@2"] = "9702:5bd6e6e4a5c44fb927306e7c",
            ["grab_snap@3"] = "9702:2c4aa69cbb4881d69d3af34e",
            ["grab_snap@4"] = "9702:6f67d3a48fdc870bfc5ba2d7",
            ["camera_switch@0"] = "15876:ef0fe7fac9773f8cad56f28c",
            ["camera_switch@1"] = "15876:f7b1dc1fe80fb20650d22ba1",
            ["camera_switch@2"] = "15876:fa58044d4e544b40e0b46efe",
            ["camera_switch@3"] = "15876:f261d55b9e5c90ff075e42a4",
            ["camera_switch@4"] = "15876:04081e7d5ea0ec4e8e4f8edc",
            ["camera_static@0"] = "88200:0d8f3599467cbecb6f61186d",
            ["camera_static@1"] = "88200:e61d6f61f34c07089bd89245",
            ["camera_static@2"] = "88200:40d96e2d9f74cde24b146b64",
            ["camera_static@3"] = "88200:3815b491b24eb13a96b8d8b7",
            ["camera_static@4"] = "88200:da88554f607388e96b3bc57d",
            ["low_thump@0"] = "37485:2326c468ef04b640bcfd0401",
            ["low_thump@1"] = "37485:728038791e3458dc250a42ff",
            ["low_thump@2"] = "37485:65662e6fca44700e0582f46a",
            ["low_thump@3"] = "37485:f645b574a6ff5ded871f71f4",
            ["low_thump@4"] = "37485:4a45a008f146e765dcdf2cdd",
            ["door_distant@0"] = "70560:5af49aab0f69951bd161eccd",
            ["door_distant@1"] = "70560:3df9122769258ef7f952a394",
            ["door_distant@2"] = "70560:a4693f1bc56e33376e484eb1",
            ["door_distant@3"] = "70560:2e2e4b14d1460c44d128e6d9",
            ["door_distant@4"] = "70560:3d54cee52a23e219504d8b3c",
            ["footstep_distant@0"] = "24255:e35898b0d5be24a1b4141db5",
            ["footstep_distant@1"] = "24255:a23e524682e52868ee2cf847",
            ["footstep_distant@2"] = "24255:f9f4f8bd65c02e679e578fa6",
            ["footstep_distant@3"] = "24255:bcad69caa7acfcee8b6608e3",
            ["footstep_distant@4"] = "24255:bbd4d631b8db3070ab9ee4a3",
            ["power_down@0"] = "114660:31a3c40771509a6805de2a96",
            ["power_down@1"] = "114660:eb050f01419f741e5494b19d",
            ["power_down@2"] = "114660:ca8737c2f95b5025d542c09c",
            ["power_down@3"] = "114660:b6edaef4fb196e4c0a7ae46b",
            ["power_down@4"] = "114660:d4c7ce23e2ccba78361e8207",
            ["end_tone@0"] = "264600:e0a9619bf0f87d08acd0efda",
            ["end_tone@1"] = "264600:dc143400034d3e1b0ef6a65b",
            ["end_tone@2"] = "264600:f78362f93d37ebdd217e6583",
            ["end_tone@3"] = "264600:167d045c0f8f18ca3a8aedf8",
            ["end_tone@4"] = "264600:8b85238f7de861857a4c3312",
            ["phone_ring@0"] = "88200:85d9f98c2e7710b9eda885de",
            ["phone_ring@1"] = "88200:85d9f98c2e7710b9eda885de",
            ["phone_ring@2"] = "88200:85d9f98c2e7710b9eda885de",
            ["phone_ring@3"] = "88200:85d9f98c2e7710b9eda885de",
            ["phone_ring@4"] = "88200:85d9f98c2e7710b9eda885de",
            ["knock_door@0"] = "97020:96aed6f4885aa683af71ed39",
            ["knock_door@1"] = "97020:7984e9526030d736ccba7a80",
            ["knock_door@2"] = "97020:83c55d01ff17d54185dc0e38",
            ["knock_door@3"] = "97020:aba0892561a659ef3c1e7d0c",
            ["knock_door@4"] = "97020:a0db457c03f8cd50e24b77f8",
            ["chair_creak@0"] = "61740:f2ca6e061cfc08fc9db8ca43",
            ["chair_creak@1"] = "61740:2638eb75557203ebf6ba074b",
            ["chair_creak@2"] = "61740:2628977bfc0a7abcbd5c8ac9",
            ["chair_creak@3"] = "61740:d8c0e698749040d720c4f3f7",
            ["chair_creak@4"] = "61740:dfed48f099ff57b2aee091b0",
            ["breath_near@0"] = "105840:d6a576ec08ae58357c832a8c",
            ["breath_near@1"] = "105840:2ced16e02e024688062931d3",
            ["breath_near@2"] = "105840:5036c70ec4f6a5311a07d8f3",
            ["breath_near@3"] = "105840:3f6336fdd0d6924ac7c65169",
            ["breath_near@4"] = "105840:356169180e1f8b6f1a76f140",
            ["whisper_burst@0"] = "57330:967b4fd4085b520b4cb932e9",
            ["whisper_burst@1"] = "57330:84b75a9560115c717bcd7487",
            ["whisper_burst@2"] = "57330:b30b22dc41b672cb513bdb2b",
            ["whisper_burst@3"] = "57330:02c8bd90b71bb951ac0534d2",
            ["whisper_burst@4"] = "57330:ff6c561cbc4dd74a559dfbdf",
            ["key_tap_rev@0"] = "31752:4ac1fff2a1a0dacc806ea0cf",
            ["key_tap_rev@1"] = "31752:1fe90632b45dab2cce8a232c",
            ["key_tap_rev@2"] = "31752:6a918eea97cc68390ba796ca",
            ["key_tap_rev@3"] = "31752:d1a9a01d451a7c28d649406b",
            ["key_tap_rev@4"] = "31752:84665af2c1a803362b2401fd",
            ["click_wrong@0"] = "17640:4fa056de07605ce169b09366",
            ["click_wrong@1"] = "17640:2609e83ae1e7fabdc0e77733",
            ["click_wrong@2"] = "17640:d55a13c970b251a14d8a7eeb",
            ["click_wrong@3"] = "17640:a3910ea23a4be43924f3dce8",
            ["click_wrong@4"] = "17640:3555ad06610f8dca6e3c502a",
            ["metal_scrape@0"] = "123480:3784d373ee24925d99561092",
            ["metal_scrape@1"] = "123480:c11bb41f0f5b53d54aed0fa3",
            ["metal_scrape@2"] = "123480:a485f6f6097b0273dfebdab0",
            ["metal_scrape@3"] = "123480:8042a489dfdc0a68fec832ee",
            ["metal_scrape@4"] = "123480:512ef3fdb5c1513b1f7a449f",
            ["step_near@0"] = "30870:0ac0fae65906eba6ca918e5a",
            ["step_near@1"] = "30870:ce7d63a22294c92f4aa4f948",
            ["step_near@2"] = "30870:f12fe2594031411e8d861ac0",
            ["step_near@3"] = "30870:4377a61a54e704478e1dfe9b",
            ["step_near@4"] = "30870:13f9f5d470d902a6a022fdf6",
            ["sub_swell@0"] = "176400:0745cfce8e73cdb47de69521",
            ["sub_swell@1"] = "176400:21faee797a613578311ccfac",
            ["sub_swell@2"] = "176400:94477f3e28aee52eec14bf42",
            ["sub_swell@3"] = "176400:654d3f6e54c402691b43ecd0",
            ["sub_swell@4"] = "176400:642993a85f0ebd582006a40c",
            ["crt_whine_rise@0"] = "154350:1d2e39b07394af5186a6bbcd",
            ["crt_whine_rise@1"] = "154350:cf83db27807b538564ad764d",
            ["crt_whine_rise@2"] = "154350:705a98bfcee3c587b199d7db",
            ["crt_whine_rise@3"] = "154350:322159a6e2e818e6dffbb837",
            ["crt_whine_rise@4"] = "154350:f19eba365a93f2935da005b7",
            ["scare_hit@0"] = "114660:90f62386d14b25f1cacb0791",
            ["scare_hit@1"] = "114660:5c3868b1e4e396896445c644",
            ["scare_hit@2"] = "114660:8fa5cdcd598c3c62b424ec61",
            ["scare_hit@3"] = "114660:ef84566c0fb4c118090aca7a",
            ["scare_hit@4"] = "114660:56e3e1b3863523b5ea6cbc57",
            ["scare_hit_soft@0"] = "114660:b821113b7471a2576bcfeeb7",
            ["scare_hit_soft@1"] = "114660:d9a20ce7386726190b1f6298",
            ["scare_hit_soft@2"] = "114660:3d5df02aa6e64e12d250e4d3",
            ["scare_hit_soft@3"] = "114660:14fe617f911c1d9238592ecd",
            ["scare_hit_soft@4"] = "114660:0c812b1a2726949a6a09a890",
            ["ear_ring@0"] = "198450:5682cf26edf855472e6dbf43",
            ["ear_ring@1"] = "198450:387b20fe443735e705a681a7",
            ["ear_ring@2"] = "198450:a9ae2eb361659c37971e65f9",
            ["ear_ring@3"] = "198450:6123a20964ab4af3142c6ac4",
            ["ear_ring@4"] = "198450:ee88c1932001e0f68ecb01cd",
            ["static_burst@0"] = "22050:28ee08022f0a1b43f89fefc2",
            ["static_burst@1"] = "22050:63ed3fc4f98ae354a641c086",
            ["static_burst@2"] = "22050:1d0e86356ed88d4e7bfc9bee",
            ["static_burst@3"] = "22050:f92b8609eda739a80518797d",
            ["static_burst@4"] = "22050:5415a5c2f49b155fd74fb61b",
        };
    }
}
