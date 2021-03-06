using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Pathoschild.Stardew.LookupAnything.Framework.Constants;
using Pathoschild.Stardew.LookupAnything.Framework.Data;
using Pathoschild.Stardew.LookupAnything.Framework.DebugFields;
using Pathoschild.Stardew.LookupAnything.Framework.Fields;
using Pathoschild.Stardew.LookupAnything.Framework.Models;
using StardewModdingAPI;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Characters;
using StardewValley.Monsters;

namespace Pathoschild.Stardew.LookupAnything.Framework.Subjects
{
    /// <summary>Describes an NPC (including villagers, monsters, and pets).</summary>
    internal class CharacterSubject : BaseSubject
    {
        /*********
        ** Properties
        *********/
        /// <summary>The NPC type.s</summary>
        private readonly TargetType TargetType;

        /// <summary>The lookup target.</summary>
        private readonly NPC Target;

        /// <summary>Simplifies access to private game code.</summary>
        private readonly IReflectionHelper Reflection;


        /*********
        ** Public methods
        *********/
        /// <summary>Construct an instance.</summary>
        /// <param name="npc">The lookup target.</param>
        /// <param name="type">The NPC type.</param>
        /// <param name="metadata">Provides metadata that's not available from the game data directly.</param>
        /// <param name="translations">Provides translations stored in the mod folder.</param>
        /// <param name="reflectionHelper">Simplifies access to private game code.</param>
        /// <remarks>Reverse engineered from <see cref="NPC"/>.</remarks>
        public CharacterSubject(NPC npc, TargetType type, Metadata metadata, ITranslationHelper translations, IReflectionHelper reflectionHelper)
            : base(translations)
        {
            this.Reflection = reflectionHelper;

            // get display type
            string typeName;
            if (type == TargetType.Villager)
                typeName = this.Text.Get(L10n.Types.Villager);
            else if (type == TargetType.Monster)
                typeName = this.Text.Get(L10n.Types.Monster);
            else
                typeName = npc.GetType().Name;

            // initialise
            this.Target = npc;
            this.TargetType = type;
            CharacterData overrides = metadata.GetCharacter(npc, type);
            string name = npc.getName();
            string description = overrides?.DescriptionKey != null ? translations.Get(overrides.DescriptionKey) : null;
            this.Initialise(name, description, typeName);
        }

        /// <summary>Get the data to display for this subject.</summary>
        /// <param name="metadata">Provides metadata that's not available from the game data directly.</param>
        public override IEnumerable<ICustomField> GetData(Metadata metadata)
        {
            NPC npc = this.Target;

            switch (this.TargetType)
            {
                case TargetType.Villager:
                    // special NPCs like Gunther
                    if (metadata.Constants.AsocialVillagers.Contains(npc.name))
                    {
                        // no data
                    }

                    // children
                    else if (npc is Child child)
                    {
                        // birthday
                        SDate birthday = SDate.Now().AddDays(-child.daysOld);
                        yield return new GenericField(this.Text.Get(L10n.Npc.Birthday), this.Text.Stringify(birthday, withYear: true));

                        // age
                        {
                            ChildAge stage = (ChildAge)child.age;
                            int daysOld = child.daysOld;
                            int daysToNext = this.GetDaysToNextChildGrowth(stage, daysOld);
                            bool isGrown = daysToNext == -1;
                            int daysAtNext = daysOld + (isGrown ? 0 : daysToNext);

                            string ageLabel = this.Translate(L10n.NpcChild.Age);
                            string ageName = this.Translate(L10n.For(stage));
                            string ageDesc = isGrown
                                ? this.Translate(L10n.NpcChild.AgeDescriptionGrown, new { label = ageName })
                                : this.Translate(L10n.NpcChild.AgeDescriptionPartial, new { label = ageName, count = daysToNext, nextLabel = this.Text.Get(L10n.For(stage + 1)) });

                            yield return new PercentageBarField(ageLabel, child.daysOld, daysAtNext, Color.Green, Color.Gray, ageDesc);
                        }

                        // friendship
                        if (Game1.player.friendships.ContainsKey(child.name))
                        {
                            FriendshipModel friendship = DataParser.GetFriendshipForVillager(Game1.player, child, metadata);
                            yield return new CharacterFriendshipField(this.Translate(L10n.Npc.Friendship), friendship, this.Text);
                            yield return new GenericField(this.Translate(L10n.Npc.TalkedToday), this.Stringify(Game1.player.friendships[child.name][2] == 1));
                        }
                    }

                    // villagers
                    else
                    {
                        // birthday
                        if (npc.birthday_Season != null)
                        {
                            SDate birthday = new SDate(npc.birthday_Day, npc.birthday_Season);
                            yield return new GenericField(this.Text.Get(L10n.Npc.Birthday), this.Text.Stringify(birthday));
                        }

                        // friendship
                        if (Game1.player.friendships.ContainsKey(npc.name))
                        {
                            FriendshipModel friendship = DataParser.GetFriendshipForVillager(Game1.player, npc, metadata);
                            yield return new GenericField(this.Translate(L10n.Npc.CanRomance), friendship.IsSpouse ? this.Translate(L10n.Npc.CanRomanceMarried) : this.Stringify(npc.datable));
                            yield return new CharacterFriendshipField(this.Translate(L10n.Npc.Friendship), friendship, this.Text);
                            yield return new GenericField(this.Translate(L10n.Npc.TalkedToday), this.Stringify(Game1.player.friendships[npc.name][2] == 1));
                            yield return new GenericField(this.Translate(L10n.Npc.GiftedToday), this.Stringify(Game1.player.friendships[npc.name][3] > 0));
                            if (!friendship.IsSpouse)
                                yield return new GenericField(this.Translate(L10n.Npc.GiftedThisWeek), this.Translate(L10n.Generic.Ratio, new { value = Game1.player.friendships[npc.name][1], max = NPC.maxGiftsPerWeek }));
                        }
                        else
                            yield return new GenericField(this.Translate(L10n.Npc.Friendship), this.Translate(L10n.Npc.FriendshipNotMet));

                        // gift tastes
                        var giftTastes = this.GetGiftTastes(npc, metadata);
                        yield return new CharacterGiftTastesField(this.Translate(L10n.Npc.LovesGifts), giftTastes, GiftTaste.Love);
                        yield return new CharacterGiftTastesField(this.Translate(L10n.Npc.LikesGifts), giftTastes, GiftTaste.Like);
                    }
                    break;

                case TargetType.Pet:
                    Pet pet = (Pet)npc;
                    yield return new CharacterFriendshipField(this.Translate(L10n.Pet.Love), DataParser.GetFriendshipForPet(Game1.player, pet), this.Text);
                    yield return new GenericField(this.Translate(L10n.Pet.PettedToday), this.Stringify(this.Reflection.GetPrivateValue<bool>(pet, "wasPetToday")));
                    break;

                case TargetType.Monster:
                    // basic info
                    Monster monster = (Monster)npc;
                    yield return new GenericField(this.Translate(L10n.Monster.Invincible), this.Translate(L10n.Generic.Seconds, new { count = this.Reflection.GetPrivateValue<int>(monster, "invincibleCountdown") }), hasValue: monster.isInvincible());
                    yield return new PercentageBarField(this.Translate(L10n.Monster.Health), monster.health, monster.maxHealth, Color.Green, Color.Gray, this.Translate(L10n.Generic.PercentRatio, new { percent = Math.Round((monster.health / (monster.maxHealth * 1f) * 100)), value = monster.health, max = monster.maxHealth }));
                    yield return new ItemDropListField(this.Translate(L10n.Monster.Drops), this.GetMonsterDrops(monster), this.Text, defaultText: this.Translate(L10n.Monster.DropsNothing));
                    yield return new GenericField(this.Translate(L10n.Monster.Experience), this.Stringify(monster.experienceGained));
                    yield return new GenericField(this.Translate(L10n.Monster.Defence), this.Stringify(monster.resilience));
                    yield return new GenericField(this.Translate(L10n.Monster.Attack), this.Stringify(monster.damageToFarmer));

                    // Adventure Guild quest
                    AdventureGuildQuestData adventureGuildQuest = metadata.GetAdventurerGuildQuest(monster.name);
                    if (adventureGuildQuest != null)
                    {
                        int kills = adventureGuildQuest.Targets.Select(p => Game1.stats.getMonstersKilled(p)).Sum();
                        yield return new GenericField(this.Translate(L10n.Monster.AdventureGuild), $"{this.Translate(kills >= adventureGuildQuest.RequiredKills ? L10n.Monster.AdventureGuildComplete : L10n.Monster.AdventureGuildIncomplete)} ({this.Translate(L10n.Monster.AdventureGuildProgress, new { count = kills, requiredCount = adventureGuildQuest.RequiredKills })})");
                    }
                    break;
            }
        }

        /// <summary>Get raw debug data to display for this subject.</summary>
        /// <param name="metadata">Provides metadata that's not available from the game data directly.</param>
        public override IEnumerable<IDebugField> GetDebugFields(Metadata metadata)
        {
            NPC target = this.Target;
            Pet pet = target as Pet;

            // pinned fields
            yield return new GenericDebugField("facing direction", this.Stringify((FacingDirection)target.FacingDirection), pinned: true);
            yield return new GenericDebugField("walking towards player", this.Stringify(target.IsWalkingTowardPlayer), pinned: true);
            if (Game1.player.friendships.ContainsKey(target.name))
            {
                FriendshipModel friendship = DataParser.GetFriendshipForVillager(Game1.player, target, metadata);
                yield return new GenericDebugField("friendship", $"{friendship.Points} (max {friendship.MaxPoints})", pinned: true);
            }
            if (pet != null)
                yield return new GenericDebugField("friendship", $"{pet.friendshipTowardFarmer} of {Pet.maxFriendship})", pinned: true);

            // raw fields
            foreach (IDebugField field in this.GetDebugFieldsFrom(target))
                yield return field;
        }

        /// <summary>Get a monster's possible drops.</summary>
        /// <param name="monster">The monster whose drops to get.</param>
        private IEnumerable<ItemDropData> GetMonsterDrops(Monster monster)
        {
            int[] drops = monster.objectsToDrop.ToArray();
            ItemDropData[] possibleDrops = DataParser.GetMonsters().First(p => p.Name == monster.name).Drops;

            return (
                from possibleDrop in possibleDrops
                let isGuaranteed = drops.Contains(possibleDrop.ItemID)
                select new ItemDropData(possibleDrop.ItemID, possibleDrop.MaxDrop, isGuaranteed ? 1 : possibleDrop.Probability)
            );
        }

        /// <summary>Draw the subject portrait (if available).</summary>
        /// <param name="spriteBatch">The sprite batch being drawn.</param>
        /// <param name="position">The position at which to draw.</param>
        /// <param name="size">The size of the portrait to draw.</param>
        /// <returns>Returns <c>true</c> if a portrait was drawn, else <c>false</c>.</returns>
        public override bool DrawPortrait(SpriteBatch spriteBatch, Vector2 position, Vector2 size)
        {
            NPC npc = this.Target;

            // use character portrait (most NPCs)
            if (npc.Portrait != null)
            {
                spriteBatch.DrawSprite(npc.Portrait, new Rectangle(0, 0, NPC.portrait_width, NPC.portrait_height), position.X, position.Y, Color.White, size.X / NPC.portrait_width);
                return true;
            }

            // else draw sprite (e.g. for pets)
            npc.Sprite.draw(spriteBatch, position, 1, 0, 0, Color.White, scale: size.X / npc.Sprite.getWidth());
            return true;
        }


        /*********
        ** Private methods
        *********/
        /// <summary>Get how much an NPC likes receiving each item as a gift.</summary>
        /// <param name="npc">The NPC.</param>
        /// <param name="metadata">Provides metadata that's not available from the game data directly.</param>
        private IDictionary<GiftTaste, Item[]> GetGiftTastes(NPC npc, Metadata metadata)
        {
            return GameHelper.GetGiftTastes(npc, metadata)
                .GroupBy(entry => entry.Value) // gift taste
                .ToDictionary(
                    tasteGroup => tasteGroup.Key, // gift taste
                    tasteGroup => tasteGroup.Select(entry => (Item)entry.Key).ToArray() // items
                );
        }

        /// <summary>Get the number of days until a child grows to the next stage.</summary>
        /// <param name="stage">The child's current growth stage.</param>
        /// <param name="daysOld">The child's current age in days.</param>
        /// <returns>Returns a number of days, or <c>-1</c> if the child won't grow any further.</returns>
        /// <remarks>Derived from <see cref="Child.dayUpdate"/>.</remarks>
        private int GetDaysToNextChildGrowth(ChildAge stage, int daysOld)
        {
            switch (stage)
            {
                case ChildAge.Newborn:
                    return 13 - daysOld;
                case ChildAge.Baby:
                    return 27 - daysOld;
                case ChildAge.Crawler:
                    return 55 - daysOld;
                default:
                    return -1;
            }
        }
    }
}
