using System.Collections;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using PleaseDontDrown.Core;
using PleaseDontDrown.Interaction;
using PleaseDontDrown.Player;
using PleaseDontDrown.Story;
using PleaseDontDrown.UI;
using UnityEngine;

namespace PleaseDontDrown.Fun
{
    /// <summary>
    /// Blackjack at the hotel casino (a side quest, not an income: the house edge is real), played right on the table
    /// (the user, 2026-10-12: not in a pop-up). Stand at the half-moon table and E deals: your cards land on the felt in
    /// front of your place, the dealer's in front of the dealer (the hole card face down), your bet in chips on the
    /// circle, the totals over the cards; everyone round the table sees every hand. Then E hits, the other button stands,
    /// Reload doubles; between hands the other button changes the bet (from the team's wallet).
    /// The dealer draws to 16 and stands on all 17s; a natural blackjack pays 3 to 2, a win 1 to 1, a push gives the bet
    /// back. The host deals and keeps the shoe (nobody can peek at the hole card); each player plays their own hand.
    /// After the gala robbery (beat 2.6 on) the casino is closed.
    /// </summary>
    public class BlackjackTable : NetworkBehaviour, IInteractionHandler, IInteractionSecondary
    {
        private static readonly int[] Bets = { 10, 25, 50, 100 };
        private const float Reach = 4f;
        private const int Seats = 5;

        [Tooltip("The middle of the felt's straight (dealer's) edge, +x out over the felt toward the players (the builder sets it).")]
        [SerializeField] private Transform _felt;
        [Tooltip("WorldText material, for the totals over the cards.")]
        [SerializeField] private Material _textMaterial;

        // ------------------------------------------------------------------ the host's games

        private class Game
        {
            public readonly List<byte> Player = new(), Dealer = new();
            public int Bet;
            public bool Over;
            public byte Seat;
        }

        private readonly Dictionary<int, Game> _games = new();
        private readonly List<byte> _shoe = new();
        private System.Random _rng;

        // ------------------------------------------------------------------ the local player

        private int _betIndex;
        private byte[] _myCards = new byte[0];
        private bool _inHand;
        private int _shownBet;
        private float _nextPress;

        // Open through the gala, closed for good once the pirates have robbed it (the team can't lose its money here then).
        private static bool Closed => StoryDirector.Instance != null && StoryDirector.Instance.CasinoClosed;

        private int Bet => Bets[Mathf.Clamp(_betIndex, 0, Bets.Length - 1)];

        public bool CanInteract(PlayerHub player) => player != null && (player.transform.position - transform.position).sqrMagnitude < Reach * Reach;

        public string GetPrompt(PlayerHub player)
        {
            if (Closed) return "Blackjack (closed since the robbery)";
            if (_inHand) return _myCards.Length == 2 && Economy.Money >= _shownBet ? $"Hit ({Total(_myCards)})   R: double" : $"Hit ({Total(_myCards)})";
            return Economy.Money >= Bet ? $"Deal blackjack, bet ${Bet}" : $"Blackjack: ${Bet} bet, wallet ${Economy.Money}";
        }

        public void OnInteract(PlayerHub player)
        {
            if (player == null || !player.IsOwner || !Pressed()) return;
            if (Closed)
            {
                PlayerHud.ShowToast("The casino's closed: the pirates cleaned it out.", 3f);
                return;
            }
            if (_inHand) HitServer();
            else if (Economy.Money < Bet) PlayerHud.ShowToast($"Not enough in the wallet for ${Bet}.", 2f);
            else DealServer(Bet);
        }

        // The other button: stand in a hand, a different bet between hands.
        public bool CanSecondary(PlayerHub player) => !Closed && CanInteract(player);
        public string GetSecondaryPrompt(PlayerHub player) => _inHand ? "Stand" : $"Bet ${Bets[(_betIndex + 1) % Bets.Length]}";

        public void OnSecondary(PlayerHub player)
        {
            if (player == null || !player.IsOwner || !Pressed()) return;
            if (_inHand) StandServer();
            else _betIndex = (_betIndex + 1) % Bets.Length;
        }

        // (One action per press: a held button doesn't hit three times.)
        private bool Pressed()
        {
            if (Time.time < _nextPress) return false;
            _nextPress = Time.time + 0.25f;
            return true;
        }

        private void Update()
        {
            PlayerHub me = PlayerHub.Local;
            if (me == null) return;
            bool near = (me.transform.position - transform.position).sqrMagnitude < (Reach + 1f) * (Reach + 1f);
            if (_inHand && near && GameInput.GameplayActive && GameInput.Reload.WasPressedThisFrame() && _myCards.Length == 2 && Pressed())
                DoubleServer();
            UpdateViews();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            DevCommands.Register("blackjack", "<deal [bet]|hit|stand|double>", "Play the nearest blackjack table (tests).", args =>
            {
                if (args.Length == 0) throw new System.ArgumentException("deal, hit, stand or double?");
                switch (args[0])
                {
                    case "deal": DealServer(args.Length > 1 ? Mathf.RoundToInt(DevCommands.ParseFloat(args, 1)) : 10); break;
                    case "hit": HitServer(); break;
                    case "stand": StandServer(); break;
                    case "double": DoubleServer(); break;
                }
            }, cheat: true, owner: this);
            DevCommands.Register("bjtest", "[bet]", "Walk up to the blackjack table and play one hand by the book (hit under 17), then print the result.", args =>
                StartCoroutine(PlayOneHand(args.Length > 0 ? Mathf.RoundToInt(DevCommands.ParseFloat(args, 0)) : 25)), cheat: true, owner: this);
        }

        /// <summary>The 'bjtest' command: stand at the felt, deal, hit under 17, stand, print what happened.</summary>
        private IEnumerator PlayOneHand(int bet)
        {
            PlayerHub me = PlayerHub.Local;
            if (me == null || _felt == null) yield break;
            // The players' side of the felt, looking at the dealer's edge.
            Vector3 side = _felt.right;
            side.y = 0f;
            Vector3 stand = _felt.position + side.normalized * 1.45f;
            // On the casino floor under that spot (the felt is about a metre up).
            if (Physics.Raycast(stand + Vector3.up * 0.3f, Vector3.down, out RaycastHit floor, 3f, ~0, QueryTriggerInteraction.Ignore))
                stand = floor.point + Vector3.up * 0.05f;
            me.Motor.Teleport(stand);
            me.Look.LookAt(_felt.position + side.normalized * 0.45f);
            yield return new WaitForSeconds(1f);
            int money = Economy.Money;
            DealServer(bet);
            yield return new WaitForSeconds(1.2f);
            for (int i = 0; i < 6 && _inHand && Total(_myCards) < 17; i++)
            {
                HitServer();
                yield return new WaitForSeconds(1.2f);
            }
            if (_inHand) StandServer();
            yield return new WaitForSeconds(1.5f);
            Debug.Log($"[Blackjack] bjtest: you {Total(_myCards)}, wallet ${money} -> ${Economy.Money}");
        }

        // ------------------------------------------------------------------ host: dealing

        private byte Draw()
        {
            if (_shoe.Count < 15)
            {
                _rng ??= new System.Random(System.Environment.TickCount);
                _shoe.Clear();
                for (int deck = 0; deck < 4; deck++)
                    for (byte c = 0; c < 52; c++) _shoe.Add(c);
                for (int i = _shoe.Count - 1; i > 0; i--)
                {
                    int j = _rng.Next(i + 1);
                    (_shoe[i], _shoe[j]) = (_shoe[j], _shoe[i]);
                }
            }
            byte card = _shoe[_shoe.Count - 1];
            _shoe.RemoveAt(_shoe.Count - 1);
            return card;
        }

        /// <summary>A hand's best total (aces 11 unless that busts).</summary>
        public static int Total(IReadOnlyList<byte> cards)
        {
            int total = 0, aces = 0;
            foreach (byte c in cards)
            {
                int rank = c % 13; // 0 = ace, 1..9 = 2..10, 10..12 = J Q K
                total += rank == 0 ? 11 : rank >= 9 ? 10 : rank + 1;
                if (rank == 0) aces++;
            }
            while (total > 21 && aces-- > 0) total -= 10;
            return total;
        }

        private static bool Natural(IReadOnlyList<byte> cards) => cards.Count == 2 && Total(cards) == 21;

        private bool Near(NetworkConnection caller, out PlayerHub player)
        {
            player = null;
            foreach (PlayerHub p in PlayerHub.All)
                if (p != null && p.Owner == caller) player = p;
            return player != null && (player.transform.position - transform.position).sqrMagnitude < (Reach + 2f) * (Reach + 2f);
        }

        [ServerRpc(RequireOwnership = false)]
        private void DealServer(int bet, NetworkConnection caller = null)
        {
            if (caller == null || !Near(caller, out PlayerHub player) || Closed) return;
            if (_games.TryGetValue(caller.ClientId, out Game old) && !old.Over) return; // finish that hand first
            bet = Mathf.Clamp(bet, Bets[0], Bets[Bets.Length - 1]);
            if (Economy.Instance == null || !Economy.Instance.ServerSpend(bet, "blackjack bet"))
            {
                Tell(caller, $"Not enough in the wallet for ${bet}.");
                return;
            }
            var game = new Game { Bet = bet, Seat = SeatFor(caller.ClientId, player) };
            game.Player.Add(Draw());
            game.Dealer.Add(Draw());
            game.Player.Add(Draw());
            game.Dealer.Add(Draw());
            _games[caller.ClientId] = game;
            if (Natural(game.Player) || Natural(game.Dealer)) Finish(caller, game);
            else Send(caller, game, "Hit or stand?");
        }

        /// <summary>The place at the table for this player: the one they had, or the free one nearest where they stand.</summary>
        private byte SeatFor(int clientId, PlayerHub player)
        {
            if (_games.TryGetValue(clientId, out Game had)) return had.Seat;
            var taken = new HashSet<byte>();
            foreach (KeyValuePair<int, Game> g in _games)
                if (g.Key != clientId) taken.Add(g.Value.Seat);
            byte best = 2;
            float bestDistance = float.MaxValue;
            for (byte i = 0; i < Seats; i++)
            {
                if (taken.Contains(i)) continue;
                float d = (SeatPoint(i, 2.0f) - player.transform.position).sqrMagnitude;
                if (d < bestDistance) { bestDistance = d; best = i; }
            }
            return best;
        }

        [ServerRpc(RequireOwnership = false)]
        private void HitServer(NetworkConnection caller = null)
        {
            if (caller == null || !_games.TryGetValue(caller.ClientId, out Game game) || game.Over) return;
            game.Player.Add(Draw());
            if (Total(game.Player) > 21) Finish(caller, game);
            else if (Total(game.Player) == 21) Finish(caller, game); // 21: nothing better to do than stand
            else Send(caller, game, "Hit or stand?");
        }

        [ServerRpc(RequireOwnership = false)]
        private void StandServer(NetworkConnection caller = null)
        {
            if (caller == null || !_games.TryGetValue(caller.ClientId, out Game game) || game.Over) return;
            Finish(caller, game);
        }

        [ServerRpc(RequireOwnership = false)]
        private void DoubleServer(NetworkConnection caller = null)
        {
            if (caller == null || !_games.TryGetValue(caller.ClientId, out Game game) || game.Over || game.Player.Count != 2) return;
            if (Economy.Instance == null || !Economy.Instance.ServerSpend(game.Bet, "blackjack double"))
            {
                Tell(caller, "Not enough in the wallet to double.");
                return;
            }
            game.Bet *= 2;
            game.Player.Add(Draw());
            Finish(caller, game);
        }

        /// <summary>The dealer plays (if the player didn't bust), the hand is settled and paid.</summary>
        private void Finish(NetworkConnection caller, Game game)
        {
            game.Over = true;
            int me = Total(game.Player);
            bool natural = Natural(game.Player), dealerNatural = Natural(game.Dealer);
            if (me <= 21 && !natural && !dealerNatural)
                while (Total(game.Dealer) < 17) game.Dealer.Add(Draw());
            int dealer = Total(game.Dealer);
            int pay;
            string result;
            if (me > 21) { pay = 0; result = $"Bust with {me}. The house takes ${game.Bet}."; }
            else if (natural && dealerNatural) { pay = game.Bet; result = "Both blackjack: push. Bet back."; }
            else if (natural) { pay = game.Bet + game.Bet * 3 / 2; result = $"BLACKJACK! You win ${game.Bet * 3 / 2}."; }
            else if (dealerNatural) { pay = 0; result = "Dealer has blackjack. You lose."; }
            else if (dealer > 21) { pay = game.Bet * 2; result = $"Dealer busts with {dealer}. You win ${game.Bet}!"; }
            else if (me > dealer) { pay = game.Bet * 2; result = $"{me} beats {dealer}. You win ${game.Bet}!"; }
            else if (me == dealer) { pay = game.Bet; result = $"{me} each: push. Bet back."; }
            else { pay = 0; result = $"Dealer's {dealer} beats your {me}."; }
            if (pay > 0 && Economy.Instance != null) Economy.Instance.ServerAdd(pay, "blackjack", transform.position + Vector3.up * 1.4f);
            Debug.Log($"[Blackjack] client {caller.ClientId}: {me} vs {dealer}, bet {game.Bet}, paid {pay}");
            Send(caller, game, result);
        }

        private void Send(NetworkConnection caller, Game game, string text)
        {
            byte[] dealer = game.Dealer.ToArray();
            if (!game.Over && dealer.Length > 1) dealer[1] = BlackjackCards.Back; // the hole card stays the host's
            HandObservers(caller.ClientId, game.Seat, game.Player.ToArray(), dealer, game.Over, text, game.Bet);
        }

        [TargetRpc]
        private void Tell(NetworkConnection target, string text) => PlayerHud.ShowToast(text, 2.5f);

        /// <summary>A player's hand as it stands now, for everyone at the table (the hole card's face isn't sent until the end).</summary>
        [ObserversRpc]
        private void HandObservers(int clientId, byte seat, byte[] mine, byte[] dealer, bool over, string text, int bet)
        {
            if (!over && dealer.Length > 1) dealer[1] = BlackjackCards.Back; // (the client never needs the hole card early)
            bool local = LocalConnection != null && LocalConnection.ClientId == clientId;
            if (local)
            {
                _myCards = mine;
                _inHand = !over;
                _shownBet = bet;
                if (over) PlayerHud.ShowToast(text, 3.5f);
                DevCommands.Print($"[blackjack] you {Total(mine)}, dealer {(over ? Total(dealer).ToString() : "?")}: {text}");
            }
            if (!_views.TryGetValue(clientId, out HandView view))
                _views[clientId] = view = new HandView(this);
            view.Show(seat, mine, dealer, over, text, bet);
        }

        // ------------------------------------------------------------------ the cards on the table

        private readonly Dictionary<int, HandView> _views = new();

        /// <summary>A point round the arc for place <paramref name="seat"/> (0..4, -60..60 degrees), this far out from the dealer's edge.</summary>
        private Vector3 SeatPoint(int seat, float radius)
        {
            float a = (-60f + seat * 30f) * Mathf.Deg2Rad;
            Transform f = _felt != null ? _felt : transform;
            return f.TransformPoint(new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius));
        }

        /// <summary>A hand laid on the felt without a game (review renders: ReviewCapture's "bjhand").</summary>
        public void ReviewHand(int player, byte seat, byte[] mine, byte[] dealer, bool over, string text, int bet)
        {
            if (!_views.TryGetValue(player, out HandView view)) _views[player] = view = new HandView(this);
            view.Show(seat, mine, dealer, over, text, bet);
        }

        private void UpdateViews()
        {
            foreach (HandView view in _views.Values) view.Tick();
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            DevCommands.Unregister("blackjack", this);
            DevCommands.Unregister("bjtest", this);
            foreach (HandView view in _views.Values) view.Clear();
            _views.Clear();
        }

        /// <summary>
        /// One player's hand on the felt: their cards fanned in front of their place (tops toward the dealer, so they read
        /// them the right way up), the dealer's cards for them in front of the dealer, the bet in chips on the circle, and
        /// the totals over them. A finished hand stays a while, then the cards are cleared away.
        /// </summary>
        private sealed class HandView
        {
            private readonly BlackjackTable _table;
            private readonly Transform _root;
            private readonly List<GameObject> _mine = new(), _dealer = new(), _chips = new();
            private readonly TextMesh _mineText, _dealerText;
            private float _clearAt = float.PositiveInfinity;

            public HandView(BlackjackTable table)
            {
                _table = table;
                _root = new GameObject("Hand").transform;
                _root.SetParent(table.transform, false);
                _mineText = Label("Total");
                _dealerText = Label("DealerTotal");
            }

            private TextMesh Label(string name)
            {
                var go = new GameObject(name);
                go.transform.SetParent(_root, false);
                var text = go.AddComponent<TextMesh>();
                text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                go.GetComponent<MeshRenderer>().sharedMaterial = _table._textMaterial != null ? _table._textMaterial : text.font.material;
                text.fontSize = 64;
                text.characterSize = 0.005f;
                text.anchor = TextAnchor.MiddleCenter;
                text.alignment = TextAlignment.Center;
                text.color = new Color(1f, 0.93f, 0.75f);
                return text;
            }

            public void Show(byte seat, byte[] mine, byte[] dealer, bool over, string result, int bet)
            {
                Transform felt = _table._felt != null ? _table._felt : _table.transform;
                float a = (-60f + seat * 30f) * Mathf.Deg2Rad;
                Vector3 outward = felt.TransformDirection(new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)));
                Vector3 along = Vector3.Cross(Vector3.up, outward); // across the player's place, left to right as they sit
                Quaternion faceSeat = Quaternion.LookRotation(-outward, Vector3.up); // card tops toward the dealer
                const float lift = 0.002f;

                // Theirs: fanned in front of their place.
                Lay(_mine, mine, i =>
                {
                    Vector3 p = _table.SeatPoint(seat, 0.8f) + along * ((i - (mine.Length - 1) * 0.5f) * 0.085f) + Vector3.up * (lift + i * 0.0012f);
                    return (p, faceSeat * Quaternion.Euler(0f, (i - (mine.Length - 1) * 0.5f) * 6f, 0f));
                });
                // The dealer's: in a row in front of the dealer, toward this place.
                Vector3 dealerSpot = felt.TransformPoint(new Vector3(0.26f, 0f, Mathf.Sin(a) * 0.36f));
                Vector3 dealerAlong = felt.TransformDirection(Vector3.forward);
                Quaternion faceTable = Quaternion.LookRotation(felt.TransformDirection(Vector3.left), Vector3.up);
                Lay(_dealer, dealer, i => (dealerSpot + dealerAlong * ((i - (dealer.Length - 1) * 0.5f) * 0.095f) + Vector3.up * (lift + i * 0.0012f), faceTable));

                // The bet: a stack of chips on the circle (a chip per $10, at most 10).
                int chips = Mathf.Clamp(bet / 10, bet > 0 ? 1 : 0, 10);
                while (_chips.Count < chips) _chips.Add(BlackjackCards.Chip(bet >= 100 ? 100 : bet >= 25 ? 25 : 10, _root));
                for (int i = 0; i < _chips.Count; i++)
                {
                    _chips[i].SetActive(i < chips && !over);
                    _chips[i].transform.SetPositionAndRotation(_table.SeatPoint(seat, 1.06f) + Vector3.up * (0.004f + i * 0.0082f), Quaternion.identity);
                }

                // The totals beside the cards, lying back on the felt so they read from the player's place (a TextMesh
                // reads from behind its +z: that points away from them).
                Quaternion readable = Quaternion.LookRotation(-outward, Vector3.up) * Quaternion.Euler(65f, 0f, 0f);
                float fan = (mine.Length - 1) * 0.5f * 0.085f + BlackjackCards.Width * 0.5f;
                int myTotal = Total(mine);
                _mineText.text = mine.Length == 0 ? "" : over ? $"{myTotal}  {Short(result)}" : (myTotal > 21 ? $"BUST {myTotal}" : myTotal.ToString());
                _mineText.characterSize = 0.011f;
                _mineText.anchor = TextAnchor.MiddleRight; // (it runs away from the cards)
                _mineText.transform.SetPositionAndRotation(_table.SeatPoint(seat, 0.8f) + along * (fan + 0.04f) + Vector3.up * 0.03f, readable);
                bool holeShown = dealer.Length < 2 || dealer[1] != BlackjackCards.Back;
                _dealerText.text = dealer.Length == 0 ? "" : holeShown ? $"DEALER {Total(dealer)}" : "DEALER";
                _dealerText.characterSize = 0.008f;
                _dealerText.anchor = TextAnchor.MiddleRight;
                float dealerFan = (dealer.Length - 1) * 0.5f * 0.095f + BlackjackCards.Width * 0.5f;
                _dealerText.transform.SetPositionAndRotation(dealerSpot - dealerAlong * (dealerFan + 0.04f) + Vector3.up * 0.03f, readable);
                _clearAt = over ? Time.time + 8f : float.PositiveInfinity;
                _root.gameObject.SetActive(true);
            }

            /// <summary>A few words for the result over the cards ("WIN +$25", "BUST", "PUSH", "LOSE").</summary>
            private static string Short(string result)
            {
                if (result.StartsWith("BLACKJACK")) return "BLACKJACK!";
                if (result.Contains("push")) return "PUSH";
                if (result.Contains("You win")) return "WIN";
                if (result.StartsWith("Bust")) return "BUST";
                return "LOSE";
            }

            private void Lay(List<GameObject> laid, byte[] cards, System.Func<int, (Vector3, Quaternion)> where)
            {
                while (laid.Count < cards.Length) laid.Add(BlackjackCards.Make(BlackjackCards.Back, _root));
                for (int i = 0; i < laid.Count; i++)
                {
                    bool on = i < cards.Length;
                    laid[i].SetActive(on);
                    if (!on) continue;
                    BlackjackCards.Show(laid[i], cards[i]);
                    (Vector3 p, Quaternion r) = where(i);
                    laid[i].transform.SetPositionAndRotation(p, r);
                }
            }

            public void Tick()
            {
                if (Time.time >= _clearAt)
                {
                    _root.gameObject.SetActive(false);
                    _clearAt = float.PositiveInfinity;
                }
            }

            public void Clear()
            {
                if (_root != null) Destroy(_root.gameObject);
            }
        }
    }
}
