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
    /// Blackjack at the hotel casino (a side quest, not an income: the house edge is real). Interact at the table and
    /// the card window opens: pick a bet from the team's wallet, Deal, then Hit, Stand or Double. The dealer draws to 16
    /// and stands on all 17s; a natural blackjack pays 3 to 2, a win 1 to 1, a push gives the bet back.
    /// The host deals and keeps the shoe (nobody can peek at the hole card); each player plays their own hand.
    /// After the gala robbery (beat 2.6 on) the casino is closed.
    /// </summary>
    public class BlackjackTable : NetworkBehaviour, IInteractionHandler
    {
        private static readonly int[] Bets = { 10, 25, 50, 100 };
        private const float Reach = 4f;

        // ------------------------------------------------------------------ the host's games

        private class Game
        {
            public readonly List<byte> Player = new(), Dealer = new();
            public int Bet;
            public bool Over;
        }

        private readonly Dictionary<int, Game> _games = new();
        private readonly List<byte> _shoe = new();
        private System.Random _rng;

        // ------------------------------------------------------------------ the local window

        private bool _open, _pushedUI;
        private int _betIndex;
        private byte[] _myCards = new byte[0], _dealerCards = new byte[0];
        private bool _inHand, _holeHidden;
        private string _result = "", _moneyText;
        private int _moneyShown = -1, _shownBet;

        private static bool Closed => StoryDirector.Instance != null && StoryDirector.Instance.BeatId is { } b &&
                                      (b.StartsWith("2.6") || b.StartsWith("2.7") || b.StartsWith("3.") || b.StartsWith("4."));

        public bool CanInteract(PlayerHub player) => player != null && (player.transform.position - transform.position).sqrMagnitude < Reach * Reach;
        public string GetPrompt(PlayerHub player) => Closed ? "Blackjack (closed since the robbery)" : $"Play blackjack (wallet ${Economy.Money})";

        public void OnInteract(PlayerHub player)
        {
            if (player == null || !player.IsOwner) return;
            if (Closed)
            {
                PlayerHud.ShowToast("The casino's closed: the pirates cleaned it out.", 3f);
                return;
            }
            SetOpen(true);
        }

        private void SetOpen(bool open)
        {
            if (_open == open) return;
            _open = open;
            if (open && !_pushedUI) { GameInput.PushUI(); _pushedUI = true; }
            if (!open && _pushedUI) { GameInput.PopUI(); _pushedUI = false; }
        }

        private void OnDisable() => SetOpen(false);

        private void Update()
        {
            if (!_open) return;
            PlayerHub me = PlayerHub.Local;
            if (GameInput.ToggleMenu.WasPressedThisFrame() || me == null || (me.transform.position - transform.position).sqrMagnitude > (Reach + 2f) * (Reach + 2f))
                SetOpen(false);
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
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            DevCommands.Unregister("blackjack", this);
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
                StateTarget(caller, new byte[0], new byte[0], true, false, $"Not enough in the wallet for ${bet}.", 0);
                return;
            }
            var game = new Game { Bet = bet };
            game.Player.Add(Draw());
            game.Dealer.Add(Draw());
            game.Player.Add(Draw());
            game.Dealer.Add(Draw());
            _games[caller.ClientId] = game;
            if (Natural(game.Player) || Natural(game.Dealer)) Finish(caller, game);
            else Send(caller, game, "Hit or stand?");
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
                Send(caller, game, "Not enough in the wallet to double.");
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

        private void Send(NetworkConnection caller, Game game, string text) =>
            StateTarget(caller, game.Player.ToArray(), game.Dealer.ToArray(), game.Over, !game.Over, text, game.Bet);

        [TargetRpc]
        private void StateTarget(NetworkConnection target, byte[] mine, byte[] dealer, bool over, bool holeHidden, string text, int bet)
        {
            _myCards = mine;
            _dealerCards = dealer;
            _inHand = !over;
            _holeHidden = holeHidden;
            _result = text;
            _shownBet = bet;
            DevCommands.Print($"[blackjack] you {Total(mine)}, dealer {(holeHidden ? "?" : Total(dealer).ToString())}: {text}");
        }

        // ------------------------------------------------------------------ the window

        private static readonly string[] Ranks = { "A", "2", "3", "4", "5", "6", "7", "8", "9", "10", "J", "Q", "K" };
        private static readonly string[] Suits = { "♠", "♥", "♦", "♣" };

        private void OnGUI()
        {
            if (!_open) return;
            if (_moneyShown != Economy.Money || _moneyText == null)
            {
                _moneyShown = Economy.Money;
                _moneyText = "Wallet $" + _moneyShown;
            }
            Hud.Dim(0.45f);
            const float width = 900f, height = 640f;
            var panel = new Rect((Hud.Width - width) * 0.5f, (Hud.Height - height) * 0.5f, width, height);
            Hud.Panel(panel, 0.94f);
            Hud.Fill(new Rect(panel.x + 20f, panel.y + 90f, width - 40f, 380f), new Color(0.04f, 0.32f, 0.18f, 0.95f), 18f);
            Hud.Label(new Rect(panel.x + 36f, panel.y + 18f, 400f, 56f), "BLACKJACK", 40f, Hud.Sand, TextAnchor.MiddleLeft, heavy: true, shadow: false);
            Hud.Label(new Rect(panel.xMax - 336f, panel.y + 18f, 300f, 56f), _moneyText, 28f, new Color(0.56f, 1f, 0.56f), TextAnchor.MiddleRight, heavy: true, shadow: false);

            Hud.Label(new Rect(panel.x + 44f, panel.y + 104f, 400f, 30f), _dealerCards.Length == 0 ? "DEALER" :
                $"DEALER  {(_holeHidden ? "?" : Total(_dealerCards).ToString())}", 22f, Color.white, TextAnchor.MiddleLeft, heavy: true, shadow: false);
            DrawCards(_dealerCards, panel.x + 44f, panel.y + 140f, _holeHidden);
            Hud.Label(new Rect(panel.x + 44f, panel.y + 290f, 400f, 30f), _myCards.Length == 0 ? "YOU" : $"YOU  {Total(_myCards)}" + (_shownBet > 0 ? $"   (bet ${_shownBet})" : ""),
                22f, Color.white, TextAnchor.MiddleLeft, heavy: true, shadow: false);
            DrawCards(_myCards, panel.x + 44f, panel.y + 326f, false);
            Hud.Label(new Rect(panel.x + 36f, panel.y + 478f, width - 72f, 44f), _result, 26f, Hud.Sand, TextAnchor.MiddleCenter, heavy: true, shadow: false);

            float y = panel.yMax - 92f;
            if (_inHand)
            {
                if (Hud.Button(new Rect(panel.x + 36f, y, 200f, 60f), "HIT", centred: true, primary: true)) HitServer();
                if (Hud.Button(new Rect(panel.x + 256f, y, 200f, 60f), "STAND", centred: true)) StandServer();
                GUI.enabled = _myCards.Length == 2 && Economy.Money >= _shownBet;
                if (Hud.Button(new Rect(panel.x + 476f, y, 200f, 60f), "DOUBLE", centred: true)) DoubleServer();
                GUI.enabled = true;
            }
            else
            {
                for (int i = 0; i < Bets.Length; i++)
                {
                    GUI.enabled = Economy.Money >= Bets[i];
                    if (Hud.Button(new Rect(panel.x + 36f + i * 120f, y, 108f, 60f), "$" + Bets[i], centred: true, small: true, selected: _betIndex == i)) _betIndex = i;
                }
                GUI.enabled = Economy.Money >= Bets[_betIndex];
                if (Hud.Button(new Rect(panel.x + 516f, y, 160f, 60f), "DEAL", centred: true, primary: true)) DealServer(Bets[_betIndex]);
                GUI.enabled = true;
            }
            if (Hud.Button(new Rect(panel.xMax - 196f, y, 160f, 60f), "LEAVE", centred: true)) SetOpen(false);
        }

        private static void DrawCards(byte[] cards, float x, float y, bool holeHidden)
        {
            for (int i = 0; i < cards.Length; i++)
            {
                var r = new Rect(x + i * 104f, y, 92f, 130f);
                bool hidden = holeHidden && i == 1;
                Hud.Fill(r, hidden ? new Color(0.65f, 0.08f, 0.1f) : new Color(0.98f, 0.97f, 0.94f), 10f);
                Hud.Frame(r, new Color(0f, 0f, 0f, 0.35f), 2f, 10f);
                if (hidden) continue;
                int rank = cards[i] % 13, suit = cards[i] / 13 % 4;
                Color ink = suit is 1 or 2 ? new Color(0.8f, 0.1f, 0.12f) : new Color(0.08f, 0.08f, 0.1f);
                Hud.Label(new Rect(r.x + 8f, r.y + 4f, 76f, 40f), Ranks[rank], 30f, ink, TextAnchor.UpperLeft, heavy: true, shadow: false);
                Hud.Label(new Rect(r.x, r.y + 40f, r.width, 80f), Suits[suit], 52f, ink, TextAnchor.MiddleCenter, shadow: false);
            }
        }
    }
}
