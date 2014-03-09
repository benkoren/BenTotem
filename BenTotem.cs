using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using JetBrains.Annotations;

using Loki.Bot;
using Loki.Bot.Logic.Behaviors;
using Loki.Game;
using Loki.Game.Inventory;
using Loki.Game.Objects;
using Loki.Game.Objects.Components;
using Loki.TreeSharp;
using Loki.Utilities;

using Action = Loki.TreeSharp.Action;
using Loki.Game.GameData;

namespace BenTotem
{
    [UsedImplicitly]
    public class TemplarCr : CombatRoutine
    {
        private const string spellTotemSpellName = "Flame Totem";
        private const int minimumTotemDistance = 20;

        #region Overrides of CombatRoutine

        private Composite _buff, _combat;

        /// <summary> Gets the name. </summary>
        /// <value> The name. </value>
        public override string Name { get { return "Ben's Totem Templar"; } }

        /// <summary> Gets the buff behavior. </summary>
        /// <value> The buff composite. </value>
        public override Composite Buff { get { return _buff; } }

        /// <summary> Gets the combat behavior. </summary>
        /// <value> The combat composite. </value>
        public override Composite Combat { get { return _combat; } }

        /// <summary>
        ///     Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        /// <filterpriority>2</filterpriority>
        public override void Dispose()
        {
        }

        /// <summary> Initializes this <see cref="CombatRoutine" />. </summary>
        public override void Initialize()
        {
            _buff = BuildBuff();
            _combat = BuildCombat();
        }

        #endregion

        #region Target Helpers

        /// <summary>
        ///     Returns the first target in the combat targeting list as a monster. (Null if no target)
        /// </summary>
        public Monster MainTarget { get { return Targeting.Combat.Targets.FirstOrDefault() as Monster; } }

        #endregion

        #region Buff Helpers

        private Composite BuildBuff()
        {
            // NOTE: We do not buff any auras here, because they are done for us by the core.
            return new PrioritySelector(
                );
        }

        private bool HasAura(Actor actor, string auraName, int minCharges = -1, double minSecondsLeft = -1)
        {
            Aura aura = actor.Auras.FirstOrDefault(a => a.Name == auraName || a.InternalName == auraName);

            // The actor doesn't even have the aura, so we don't need to go messing with it. :)
            if (aura == null)
            {
                return false;
            }

            // Check if mincharges needs to be ensured
            if (minCharges != -1)
            {
                // This is an exclusive check. So if we pass 3, we want to ensure we have 3 charges up.
                // Thus; 2 < 3, we don't have enough charges, and therefore we "don't have the aura" yet
                if (aura.Charges < minCharges)
                {
                    return false;
                }
            }
            // Those with R# installed, can ignore the following error.
            // ReSharper disable once CompareOfFloatsByEqualityOperator
            if (minSecondsLeft != -1)
            {
                if (aura.TimeLeft.TotalSeconds < minSecondsLeft)
                {
                    return false;
                }
            }

            // We have the aura.
            // We have enough charges.
            // And the time left is above the min seconds threshold.
            return true;
        }

        #endregion

        #region Totem Helpers

        private int _maxTotemsAllowed;
        private string _spellTotemSpellName;
        private int _totemRange;

        private WaitTimer _totemDropTimer = new WaitTimer(TimeSpan.FromSeconds(3));

        #endregion

        #region Cast Helpers

        /// <summary>
        ///     Returns whether or not the specified count of mobs are near the specified monster, within the defined range.
        /// </summary>
        /// <param name="monster"></param>
        /// <param name="distance"></param>
        /// <param name="count"></param>
        /// <returns></returns>
        private bool NumberOfMobsNear(NetworkObject monster, float distance, int count)
        {
            if (monster == null)
            {
                return false;
            }

            Vector2i mpos = monster.Position;

            int curCount = 0;
            foreach (NetworkObject mob in Targeting.Combat.Targets)
            {
                if (mob.Id == monster.Id)
                {
                    continue;
                }

                if (mob.Position.Distance(mpos) < distance)
                {
                    curCount++;
                }

                if (curCount >= count)
                {
                    return true;
                }
            }

            return false;
        }

        private Composite Cast(string spell, SpellManager.GetSelection<bool> reqs = null)
        {
            // Note: this is safe to do. If we pass null to the requirements check, that means we always want to fire
            // as long as CanCast is true.
            if (reqs == null)
            {
                reqs = ret => true;
            }

            return SpellManager.CreateSpellCastComposite(spell, reqs, ret => MainTarget);
        }

        private Composite Cast(string spell, SpellManager.GetSelection<Vector2i> location, SpellManager.GetSelection<bool> reqs = null)
        {
            // Note: this is safe to do. If we pass null to the requirements check, that means we always want to fire
            // as long as CanCast is true.
            if (reqs == null)
            {
                reqs = ret => true;
            }

            return SpellManager.CreateSpellCastComposite(spell, reqs, location);
        }

        #endregion

        #region LOS

        private Composite CreateMoveToLos()
        {
            return new Decorator(ret => !LokiPoe.MeleeLineOfSight.CanSee(LokiPoe.ObjectManager.Me.Position, MainTarget.Position),
                CommonBehaviors.MoveTo(ret => MainTarget.Position, ret => "CreateMoveToLos"));
        }

        private Composite CreateMoveToRange()
        {
            return new Decorator(ret => MainTarget.Distance > 50,
                CommonBehaviors.MoveTo(ret => MainTarget.Position, ret => "CreateMoveToRange"));
        }

        #endregion

        #region Flask Helpers

        private readonly WaitTimer _flaskCd = new WaitTimer(TimeSpan.FromSeconds(0.5));

        private IEnumerable<InventoryItem> LifeFlasks
        {
            get
            {
                IEnumerable<InventoryItem> inv = LokiPoe.ObjectManager.Me.Inventory.Flasks.Items;
                return from item in inv
                       let flask = item.Flask
                       where flask != null && flask.HealthRecover > 0 && flask.CanUse
                       orderby flask.IsInstantRecovery ? flask.HealthRecover : flask.HealthRecoveredPerSecond descending
                       select item;
            }
        }

        private IEnumerable<InventoryItem> ManaFlasks
        {
            get
            {
                IEnumerable<InventoryItem> inv = LokiPoe.ObjectManager.Me.Inventory.Flasks.Items;
                return from item in inv
                       let flask = item.Flask
                       where flask != null && flask.ManaRecover > 0 && flask.CanUse
                       orderby flask.IsInstantRecovery ? flask.ManaRecover : flask.ManaRecoveredPerSecond descending
                       select item;
            }
        }

        private IEnumerable<InventoryItem> GraniteFlasks
        {
            get
            {
                IEnumerable<InventoryItem> inv = LokiPoe.ObjectManager.Me.Inventory.Flasks.Items;
                return from item in inv
                       let flask = item.Flask
                       where flask != null && item.Name == "Granite Flask" && flask.CanUse
                       select item;
            }
        }

        private IEnumerable<InventoryItem> QuicksilverFlasks
        {
            get
            {
                //InternalName: flask_utility_sprint, BuffType: 24, CasterId: 13848, OwnerId: 0, TimeLeft: 00:00:05.0710000, Charges: 1, Description: You have greatly increased Movement Speed
                IEnumerable<InventoryItem> inv = LokiPoe.ObjectManager.Me.Inventory.Flasks.Items;
                return from item in inv
                       let flask = item.Flask
                       where flask != null && item.Name == "Quicksilver Flask" && flask.CanUse
                       select item;
            }
        }

        private Player Me { get { return LokiPoe.ObjectManager.Me; } }

        private Composite CreateFlaskLogic()
        {
            return new PrioritySelector(
                new Decorator(ret => _flaskCd.IsFinished && Me.HealthPercent < 70 && LifeFlasks.Count() != 0 && !Me.HasAura("flask_effect_life"),
                    new Action(ret =>
                    {
                        LifeFlasks.First().Use();
                        _flaskCd.Reset();
                    })),
                new Decorator(ret => _flaskCd.IsFinished && Me.ManaPercent < 20 && ManaFlasks.Count() != 0 && !Me.HasAura("flask_effect_mana"),
                    new Action(ret =>
                    {
                        ManaFlasks.First().Use();
                        _flaskCd.Reset();
                    }))
                );
        }

        #endregion

        #region Combat

        private readonly Dictionary<string, DateTime> _totemTimers = new Dictionary<string, DateTime>();

        private Composite BuildCombat()
        {
            return new PrioritySelector(
                CreateFlaskLogic(),
                CreateMoveToLos(),
                CreateMoveToRange(),
                CreateTotemLogic(),
                CreateTrapLogic(),
                CreateFallbackAttackLogic()
                );
        }

        private Composite CreateTrapLogic()
        {
            return Cast("Cold Snap",
                ret =>
                {
                    int trapRadius = 16; // Default Cold Snap radius. @TODO: Calculate this to account for increased aoe.
                    return NumberOfMobsNear(MainTarget, trapRadius, 3);
                });
        }

        private Composite CreateTotemLogic() {
            return Cast(spellTotemSpellName,
                ret => MainTarget.Position,
                ret => ShouldCastTotem(MainTarget));
        }

        private bool ShouldCastTotem(NetworkObject target)
        {
            // If it hasn't been very long, we don't want to do much processing. Check that now.

            DateTime waitUntil;
            _totemTimers.TryGetValue(spellTotemSpellName, out waitUntil);

            if (waitUntil > DateTime.Now)
            {
                return false;
            }

            Spell spell = SpellManager.GetSpell(spellTotemSpellName);
            int totemRange = spell.GetStat(StatType.TotemRange);
            var spellCastTime = (int)spell.CastTime.TotalMilliseconds;

            int maxTotemCount = spell.GetStat(StatType.SkillDisplayNumberOfTotemsAllowed);
            var currentTotems = spell.DeployedObjects;

            bool shouldcast = currentTotems.Count() < maxTotemCount // More totems are available
                || (currentTotems.Any(o => // If any totems are out of range or LOS
                        o.Position.Distance(target.Position) > minimumTotemDistance
                        || !LokiPoe.RangedLineOfSight.CanSee(o.Position, target.Position)
                    ));

            
            if (shouldcast)
            {
                DateTime castTime = DateTime.Now.AddMilliseconds(spellCastTime + 500);
                if (!_totemTimers.ContainsKey(spellTotemSpellName))
                {
                    _totemTimers.Add(spellTotemSpellName, castTime);
                }
                else
                {
                    _totemTimers[spellTotemSpellName] = castTime;
                }
                Log.Debug("••• CASTING TOTEM •••");
            }

            return shouldcast;
        }

        private Composite CreateFallbackAttackLogic()
        {
            return new PrioritySelector(
                Cast("Default Attack")
                );
        }

        #endregion
    }
}