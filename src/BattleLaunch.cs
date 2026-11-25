using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Boksol
{
    internal static class BattleLaunch
    {
        private const string Live = "BATTLE-LIVE";
        private const string Waiting = "BATTLE-WAITING";

        public static async Task<PreparedRom> Prepare(Game game, string data, GameImage image, byte[] save,
            CodeMatch match, CancellationToken stop)
        {
            byte[] checkpoint = game.IsDs && match.Count > 2 ? null :
                await Task.Run(() => BattleSaves.ReadStart(game, save, image.Hash, match.IsHost), stop);
            string[] peers = await match.ExchangeAll(checkpoint == null ? "ORIGINAL" : "CHECKPOINT", TimeSpan.FromSeconds(20), stop);
            if (peers.Any(peer => peer != "ORIGINAL" && peer != "CHECKPOINT"))
                throw new IOException("对方的对战起点不兼容。");
            if (peers.Contains("ORIGINAL")) checkpoint = null;
            string role = match.Seat == 1 ? "host" : match.Seat == 2 ? "guest" : "guest-" + match.Seat;
            return await Task.Run(() => game.PrepareBattle(data, role, image, save, checkpoint), stop);
        }

        public static async Task<string> Enter(Game game, PreparedRom rom, CoreSession session, CodeMatch match,
            CancellationToken stop)
        {
            if (!rom.HasBattleStart) return "已载入所选存档 · 请在原作内打开联机菜单";
            switch (game.Id)
            {
                case "boktai2":
                    return await StartGbaBattle(session, match, stop)
                        ? "已进入原作对战" : "原作仍在等待 · 由房主确认开战";
                case "boktai3":
                    return await EnterRaceSetup(session, match, stop);
                case "boktaids":
                    return await EnterDsLobby(game, rom, session, match, stop);
                default:
                    throw new InvalidDataException("当前作品的原作对战入口不可用。");
            }
        }

        private static async Task<string> EnterDsLobby(Game game, PreparedRom rom, CoreSession session, CodeMatch match,
            CancellationToken stop)
        {
            const string joining = "DS-JOINING", rules = "DS-RULES", handoff = "DS-HANDOFF";
            if (match.IsHost)
            {
                var entry = await DsPreBattle.TryEnter(game, rom, session, 1, true, stop);
                string peer = await match.Exchange(entry == DsBattleEntry.Rules ? rules : handoff, TimeSpan.FromSeconds(12), stop);
                if (peer != joining) throw new IOException("对方的 DS 房间准备状态不兼容。");
                if (entry == DsBattleEntry.Rules) return "设置时限、局数和场地 · 确认后各自选择角色与装备";
                return DescribeDsHandoff(entry);
            }

            using (var guestStop = CancellationTokenSource.CreateLinkedTokenSource(stop))
            {
                // The guest's waiting screen differs from the host's rules screen.
                var automatic = DsPreBattle.TryEnter(game, rom, session, 1, false, guestStop.Token);
                DsBattleEntry entry = DsBattleEntry.Manual;
                string peer;
                try { peer = await match.Exchange(joining, TimeSpan.FromSeconds(65), stop); }
                finally
                {
                    guestStop.Cancel();
                    try { entry = await automatic; }
                    catch (OperationCanceledException) when (guestStop.IsCancellationRequested) { }
                }
                stop.ThrowIfCancellationRequested();
                if (peer == rules) return "等待房主确认规则 · 随后选择角色、武器和道具";
                if (peer != handoff) throw new IOException("对方的 DS 房间准备状态不兼容。");
                return DescribeDsHandoff(entry);
            }
        }

        private static string DescribeDsHandoff(DsBattleEntry entry)
        {
            if (entry == DsBattleEntry.Loadout) return "请选择角色、武器和道具";
            if (entry == DsBattleEntry.NeedsSave) return "请在原作内选择有存档的槽位";
            return "已恢复原作对战入口 · 请在原作内继续";
        }

        private static async Task<string> EnterRaceSetup(CoreSession session, CodeMatch match, CancellationToken stop)
        {
            bool ready = match.IsHost && await GbaPreBattle.OpenRaceSetup(session, stop);
            string peer = await match.Exchange(match.IsHost ? (ready ? "RACE-SETUP" : "RACE-WAITING") : "RACE-READY",
                TimeSpan.FromSeconds(session.IsRemote ? 50 : 25), stop);
            if (match.IsHost ? peer != "RACE-READY" : peer != "RACE-SETUP" && peer != "RACE-WAITING")
                throw new IOException("对方的摩托车赛前状态无效。");
            if (!match.IsHost) return "摩托车对战 · 等待房主选择赛道";
            return ready ? "摩托车对战 · 先选赛道，随后双方配置摩托" : "摩托车对战 · 按 Start 打开赛前配置";
        }

        private static async Task<bool> StartGbaBattle(CoreSession session, CodeMatch match, CancellationToken stop)
        {
            const string startedState = "BATTLE-STARTED";
            const string watchState = "BATTLE-WATCH";
            int wait = session.IsRemote ? 50 : 27;
            bool started = match.IsHost && await GbaPreBattle.StartBattle(session, true, match.Count, stop);
            string[] peers = await match.ExchangeAll(match.IsHost ? (started ? startedState : Waiting) : watchState,
                TimeSpan.FromSeconds(wait), stop);
            if (match.IsHost ? peers.Any(peer => peer != watchState) :
                (peers[0] != startedState && peers[0] != Waiting) || peers.Skip(1).Any(peer => peer != watchState))
                throw new IOException("对方的原作对战状态无效。");
            if (!match.IsHost && peers[0] == startedState)
                started = await GbaPreBattle.StartBattle(session, false, match.Count, stop);
            return await ConfirmBattle(started, match, wait, stop);
        }

        private static async Task<bool> ConfirmBattle(bool ready, CodeMatch match, int timeout, CancellationToken stop)
        {
            string[] peers = await match.ExchangeAll(ready ? Live : Waiting, TimeSpan.FromSeconds(timeout), stop);
            if (peers.Any(peer => peer != Live && peer != Waiting)) throw new IOException("对方的原作对战状态无效。");
            return ready && peers.All(peer => peer == Live);
        }
    }
}
