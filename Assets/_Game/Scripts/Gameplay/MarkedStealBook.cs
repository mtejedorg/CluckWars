using System;

namespace CluckWars.Gameplay
{
    /// <summary>What <see cref="MarkedStealBook{TKey}.Settle"/> found.</summary>
    public enum MarkedOutcome : byte
    {
        /// <summary>No such marked steal is open (never asked, already settled, expired, wrong victim or wrong id): nothing is credited, the book is untouched.</summary>
        Unknown = 0,
        /// <summary>The victim's authority refused it (a shell, a wall): the slot is closed, nothing is credited, the mark stays.</summary>
        Blocked = 1,
        /// <summary>It reached the victim: <see cref="MarkedSettlement.Take"/> and <see cref="MarkedSettlement.Bounty"/> are to be added to the cargo and the mark is spent.</summary>
        Reached = 2,
    }

    /// <summary>What one answer to a marked steal is worth to the Thief's cargo.</summary>
    public struct MarkedSettlement
    {
        public MarkedOutcome Outcome;
        /// <summary>The stolen food that fitted: the victim's report, held to what was asked and to the free room.</summary>
        public float Take;
        /// <summary>The bounty that fitted into the room the take left. Never above what was offered.</summary>
        public float Bounty;
        /// <summary>The bounty that did not fit: lost, never held back.</summary>
        public float BountyLost;
        /// <summary>What the take lost to the free room (the victim drained more than fitted): lost like any steal overflow.</summary>
        public float TakeOverflow;
    }

    /// <summary>
    /// The Thief's side of a marked steal (BL-217): the one request in flight, its reservations in the chicken's <see cref="FoodRequests{TKey}"/> and the
    /// rule for what the victim's answer pays. Pure and Fusion-free (the key is generic), so the EditMode suite drives the very code
    /// <c>ChickenCargo</c> runs. The shape is the steal's (<see cref="FoodTransferRules"/>): ask and record, the owner debits and reports, only the report
    /// credits, once, against the open request. A marked steal is an ordinary steal plus a bonus: the take is asked first, then room for the bounty in
    /// what is left (<see cref="LootRules.MarkedGain"/>'s order), and at settlement the take is credited first and the bounty into the room left.
    /// </summary>
    /// <remarks>
    /// One slot is enough and is the double-credit guard: Steal's cooldown is longer than <see cref="FoodTransferRules.SettleWindowSeconds"/>, and
    /// <see cref="TryOpen"/> refuses a second request while one is open, so two casts can never both be paid. The reservation of both amounts keeps
    /// the other rivals of the same cast from taking the room the bounty needs.
    /// </remarks>
    public sealed class MarkedStealBook<TKey> where TKey : struct, IEquatable<TKey>
    {
        private readonly FoodRequests<TKey> _requests;

        private bool _live;
        private ushort _id;
        private ushort _nextId;
        private TKey _victim;
        private float _time;
        private bool _hasTake;
        private ushort _takeId;
        private bool _hasBounty;
        private ushort _bountyId;
        private float _offered;

        public MarkedStealBook(FoodRequests<TKey> requests)
        {
            _requests = requests ?? throw new ArgumentNullException(nameof(requests));
        }

        /// <summary>A marked steal has been asked and not answered, inside the settle window.</summary>
        public bool Pending(float now) => _live && now - _time <= FoodTransferRules.SettleWindowSeconds;

        /// <summary>
        /// Asks for a marked steal on <paramref name="victim"/>. <paramref name="amount"/> is the take the passive allows; it is held to the free room and to
        /// <paramref name="victimStealable"/> and reserved, then room for <paramref name="bounty"/> is reserved in what is left. False (nothing recorded) when
        /// another marked steal is still open. Otherwise true, <paramref name="askedTake"/> is what to ask the victim to drain (0 is valid: an empty beak or a
        /// full Thief) and <paramref name="id"/> names the request.
        /// </summary>
        public bool TryOpen(TKey victim, float amount, float victimStealable, float bounty, float capacity, float cargo, float now,
            out float askedTake, out ushort id)
        {
            askedTake = 0f;
            id = 0;
            if (Pending(now)) return false;

            _live = true;
            _id = id = _nextId++;
            _victim = victim;
            _time = now;
            _offered = Math.Max(0f, bounty);
            _hasTake = _hasBounty = false;

            float take = StealMath.Clamp(amount, _requests.FreeSpace(capacity, cargo, now), victimStealable);
            if (take > 0f && _requests.AskSteal(victim, take, capacity, cargo, now, out float asked, out ushort takeId))
            {
                _hasTake = true;
                _takeId = takeId;
                askedTake = asked;
            }
            float room = FoodTransferRules.Fit(_offered, _requests.FreeSpace(capacity, cargo, now));
            if (room > 0f && _requests.AskSteal(victim, room, capacity, cargo, now, out _, out ushort bountyId))
            {
                _hasBounty = true;
                _bountyId = bountyId;
            }
            return true;
        }

        /// <summary>
        /// The victim's answer: <paramref name="reached"/> says the steal got through, <paramref name="stolen"/> is what it really drained. Matches the one open
        /// request by victim and id inside the window and closes it, once (a repeat is <see cref="MarkedOutcome.Unknown"/>). A refusal credits nothing. A steal
        /// that reached pays the take, held to what was asked and to the free room, and then the bounty into the room that is left.
        /// </summary>
        public MarkedSettlement Settle(TKey victim, ushort id, float stolen, bool reached, float capacity, float cargo, float now)
        {
            if (!Pending(now) || _id != id || !_victim.Equals(victim)) return default;
            _live = false;

            // The bounty's reserved room goes back first, so the take is held to the free room and not to the bounty's own reservation.
            if (_hasBounty) _requests.SettleSteal(victim, _bountyId, 0f, capacity, cargo, now);
            float take = 0f, overflow = 0f;
            if (_hasTake)
            {
                var settled = _requests.SettleSteal(victim, _takeId, reached ? stolen : 0f, capacity, cargo, now);
                take = settled.ToCargo;
                overflow = settled.Overflow;
            }
            if (!reached) return new MarkedSettlement { Outcome = MarkedOutcome.Blocked, TakeOverflow = overflow };

            float bounty = FoodTransferRules.Fit(_offered, _requests.FreeSpace(capacity, cargo + take, now));
            return new MarkedSettlement { Outcome = MarkedOutcome.Reached, Take = take, Bounty = bounty, BountyLost = _offered - bounty, TakeOverflow = overflow };
        }

        /// <summary>Drops the open request (new round). Its reservations go with <see cref="FoodRequests{TKey}.Clear"/>.</summary>
        public void Clear() => _live = false;
    }
}
