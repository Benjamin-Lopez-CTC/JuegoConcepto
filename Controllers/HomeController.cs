using Microsoft.AspNetCore.Mvc;
using JuegoConcepto.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace JuegoConcepto.Controllers
{
    public class HomeController : Controller
    {
        private readonly GameDbContext _db;

        public HomeController(GameDbContext db)
        {
            _db = db;
        }

        // ─── Helpers ──────────────────────────────────────────────────────────

        private static string NormalizeColor(string color)
        {
            if (string.IsNullOrWhiteSpace(color)) return string.Empty;
            string normalized = color.Trim().ToLowerInvariant();
            normalized = normalized
                .Replace('á', 'a').Replace('é', 'e').Replace('í', 'i')
                .Replace('ó', 'o').Replace('ú', 'u').Replace('ü', 'u');
            return normalized;
        }

        // Helper para convertir el estado de la DB a la vieja ViewModel (GameState)
        // para afectar lo menos posible a la vista principal.
        private async Task<GameState> BuildViewModelAsync()
        {
            var round = await _db.Rounds
                .Include(r => r.Players)
                .Include(r => r.Games)
                .ThenInclude(g => g.ColorSubmissions)
                .OrderByDescending(r => r.Id)
                .FirstOrDefaultAsync(r => r.IsActive);

            var vm = new GameState();

            if (round == null)
            {
                vm.Phase = GamePhase.Setup;
                return vm;
            }

            vm.Players = round.Players.OrderBy(p => p.OrderIndex).Select(p => p.Name).ToList();

            var currentGame = round.Games.OrderByDescending(g => g.Id).FirstOrDefault();
            
            if (currentGame == null || currentGame.Phase == GamePhase.Won)
            {
                // Si no hay juego activo (o si ya se ganó y guardó el leaderboard),
                // estamos en el Setup listos para arrancar una partida nueva en esta Ronda.
                vm.Phase = GamePhase.Setup;
                return vm;
            }

            // Estamos jugando o esperando nombre
            vm.Phase = currentGame.Phase;
            vm.ElapsedMilliseconds = currentGame.ElapsedMilliseconds;
            vm.StartTime = currentGame.StartTime;
            vm.WinningReason = currentGame.WinningReason;
            vm.DuplicateColor = currentGame.DuplicateColor;
            vm.PlayersReached = currentGame.PlayersReached;

            // Historial de colores del juego actual
            vm.ColorHistory = currentGame.ColorSubmissions.OrderBy(cs => cs.InsertedAt).Select(cs => new PlayerEntry
            {
                PlayerName = cs.Player.Name,
                Color = cs.Color
            }).ToList();

            if (!string.IsNullOrEmpty(currentGame.SerializedRoundQueue))
            {
                vm.RoundQueue = JsonSerializer.Deserialize<List<int>>(currentGame.SerializedRoundQueue) ?? new List<int>();
            }

            // Localizar indice del jugador actual en la lista del ViewModel
            var currentPlayer = round.Players.FirstOrDefault(p => p.Id == currentGame.CurrentPlayerId);
            if (currentPlayer != null)
            {
                vm.CurrentPlayerIndex = vm.Players.IndexOf(currentPlayer.Name);
            }
            
            return vm;
        }

        // ─── GET / ────────────────────────────────────────────────────────────

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            return View(await BuildViewModelAsync());
        }

        // ─── GET /TechStack ───────────────────────────────────────────────────

        [HttpGet]
        public IActionResult TechStack()
        {
            return View();
        }

        // ─── RONDAS: POST /Home/AddPlayer ─────────────────────────────────────

        [HttpPost]
        public async Task<IActionResult> AddPlayer(string playerName)
        {
            string name = playerName?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(name)) return RedirectToAction(nameof(Index));

            var round = await _db.Rounds.Include(r => r.Players)
                .OrderByDescending(r => r.Id)
                .FirstOrDefaultAsync(r => r.IsActive);

            if (round == null)
            {
                // Crear ronda activa
                round = new Round();
                _db.Rounds.Add(round);
                await _db.SaveChangesAsync();
            }

            // No permitir si ya hay un juego corriendo
            if (round.Games.Any(g => g.Phase == GamePhase.Playing || g.Phase == GamePhase.WonPendingId))
                return RedirectToAction(nameof(Index));

            if (!round.Players.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                int nextIndex = round.Players.Count;
                round.Players.Add(new Player { Name = name, OrderIndex = nextIndex });
                await _db.SaveChangesAsync();
            }

            return RedirectToAction(nameof(Index));
        }

        // ─── RONDAS: POST /Home/RemovePlayer ──────────────────────────────────

        [HttpPost]
        public async Task<IActionResult> RemovePlayer(int index)
        {
            var round = await _db.Rounds.Include(r => r.Players)
                .OrderByDescending(r => r.Id)
                .FirstOrDefaultAsync(r => r.IsActive);

            if (round != null && !round.Games.Any(g => g.Phase == GamePhase.Playing || g.Phase == GamePhase.WonPendingId))
            {
                var pList = round.Players.OrderBy(p => p.OrderIndex).ToList();
                if (index >= 0 && index < pList.Count)
                {
                    _db.Players.Remove(pList[index]);
                    await _db.SaveChangesAsync();
                }
            }

            return RedirectToAction(nameof(Index));
        }

        // ─── JUEGO: POST /Home/StartGame ──────────────────────────────────────

        [HttpPost]
        public async Task<IActionResult> StartGame()
        {
            var round = await _db.Rounds.Include(r => r.Players)
                .OrderByDescending(r => r.Id)
                .FirstOrDefaultAsync(r => r.IsActive);

            if (round == null || round.Players.Count < 2)
                return RedirectToAction(nameof(Index));

            var players = round.Players.OrderBy(p => p.OrderIndex).ToList();
            var queue = Enumerable.Range(0, players.Count).ToList();
            Random rng = Random.Shared;
            for (int i = queue.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (queue[i], queue[j]) = (queue[j], queue[i]);
            }

            int currentIdx = queue[0];
            queue.RemoveAt(0);

            var game = new Game
            {
                RoundId = round.Id,
                Phase = GamePhase.Playing,
                StartTime = DateTime.UtcNow,
                SerializedRoundQueue = JsonSerializer.Serialize(queue),
                CurrentPlayerId = players[currentIdx].Id
            };

            _db.Games.Add(game);
            await _db.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        // ─── JUEGO: POST /Home/SubmitColor ────────────────────────────────────

        [HttpPost]
        public async Task<IActionResult> SubmitColor(string color, long elapsedMs)
        {
            var round = await _db.Rounds.Include(r => r.Players).Include(r => r.Games).ThenInclude(g => g.ColorSubmissions)
                .OrderByDescending(r => r.Id).FirstOrDefaultAsync(r => r.IsActive);

            if (round == null) return RedirectToAction(nameof(Index));

            var game = round.Games.OrderByDescending(g => g.Id).FirstOrDefault();
            if (game == null || game.Phase != GamePhase.Playing)
                return RedirectToAction(nameof(Index));

            string normalized = NormalizeColor(color);
            if (string.IsNullOrEmpty(normalized)) return RedirectToAction(nameof(Index));

            game.ElapsedMilliseconds = elapsedMs;

            // ===== REGLA NUEVA: Revisar si el color se repite en TODA LA RONDA =====
            ColorSubmission? duplicate = null;
            var orderedGames = round.Games.OrderBy(g => g.Id).ToList();
            
            foreach (var g in orderedGames)
            {
                duplicate = g.ColorSubmissions.FirstOrDefault(cs => cs.NormalizedColor == normalized);
                if (duplicate != null) break;
            }

            if (duplicate != null)
            {
                int duplicateGameNumber = orderedGames.FindIndex(g => g.Id == duplicate.GameId) + 1;
                
                game.Phase = GamePhase.Lost;
                game.DuplicateColor = color; // Usamos el original para mostrar
                game.PlayersReached = game.ColorSubmissions.Count + 1;
                game.WinningReason = $"El color \"{duplicate.Color}\" ya fue dicho por {duplicate.Player.Name} en la Partida #{duplicateGameNumber}.";
                await _db.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }

            // Guardar color
            _db.ColorSubmissions.Add(new ColorSubmission
            {
                GameId = game.Id,
                PlayerId = game.CurrentPlayerId,
                Color = color,
                NormalizedColor = normalized
            });
            await _db.SaveChangesAsync();

            var currentColorCount = await _db.ColorSubmissions.CountAsync(cs => cs.GameId == game.Id);
            game.PlayersReached = currentColorCount;

            var queue = JsonSerializer.Deserialize<List<int>>(game.SerializedRoundQueue!) ?? new List<int>();

            // ¿Terminaron la ronda sin repetir?
            if (queue.Count == 0 && currentColorCount >= round.Players.Count)
            {
                // ¡Ganaron! Pasamos a pendiente de ID para el Leaderboard
                game.Phase = GamePhase.WonPendingId;
                game.WinningReason = "¡Completaron una partida entera sin repetir colores de partidas pasadas!";
            }
            else
            {
                if (queue.Count == 0) // Nunca debería pasar acá por nuestras reglas de 1 ronda, pero por si acaso.
                    queue = CreateShuffledQueue(round.Players.Count);

                int nextIdx = queue[0];
                queue.RemoveAt(0);
                game.CurrentPlayerId = round.Players.OrderBy(p => p.OrderIndex).ElementAt(nextIdx).Id;
                game.SerializedRoundQueue = JsonSerializer.Serialize(queue);
            }

            await _db.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        // ─── GAME: POST /Home/SaveWinningId ───────────────────────────────────

        [HttpPost]
        public async Task<IActionResult> SaveWinningId(string winningIdentifier)
        {
            var game = await _db.Games.OrderByDescending(g => g.Id).FirstOrDefaultAsync();
            if (game != null && game.Phase == GamePhase.WonPendingId)
            {
                game.WinningIdentifier = winningIdentifier ?? "Equipo Anónimo";
                game.Phase = GamePhase.Won;
                await _db.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Index));
        }

        // ─── JUEGO: POST /Home/ResetGame ──────────────────────────────────────

        [HttpPost]
        public async Task<IActionResult> ResetGame()
        {
            var round = await _db.Rounds.OrderByDescending(r => r.Id).FirstOrDefaultAsync(r => r.IsActive);
            if (round != null)
            {
                var game = await _db.Games.Where(g => g.RoundId == round.Id).OrderByDescending(g => g.Id).FirstOrDefaultAsync();
                if (game != null && (game.Phase == GamePhase.Playing))
                {
                    // Si deciden reiniciar una en progreso, la marcamos como perdida
                    game.Phase = GamePhase.Lost;
                    game.WinningReason = "Reiniciado manualmente";
                    await _db.SaveChangesAsync();
                }
            }
            // Regresa a Index, lo cual en "Setup" dejará crear otro juego en la misma ronda
            return RedirectToAction(nameof(Index));
        }

        // ─── RONDAS: POST /Home/EndRound ──────────────────────────────────────

        [HttpPost]
        public async Task<IActionResult> EndRound()
        {
            var round = await _db.Rounds.OrderByDescending(r => r.Id).FirstOrDefaultAsync(r => r.IsActive);
            if (round != null)
            {
                round.EndTime = DateTime.UtcNow;
                round.IsActive = false;
                
                // Si había un juego corriendo, lo matamos
                var game = await _db.Games.Where(g => g.RoundId == round.Id).OrderByDescending(g => g.Id).FirstOrDefaultAsync();
                if (game != null && game.Phase == GamePhase.Playing)
                {
                    game.Phase = GamePhase.Lost;
                    game.WinningReason = "Ronda terminada abruptamente";
                }
                
                await _db.SaveChangesAsync();
                return RedirectToAction("RoundSummary", new { id = round.Id });
            }
            return RedirectToAction(nameof(Index));
        }

        // ─── LEADERBOARD: GET /Home/RoundSummary ──────────────────────────────

        [HttpGet]
        public async Task<IActionResult> RoundSummary(int id)
        {
            var round = await _db.Rounds.Include(r => r.Games).FirstOrDefaultAsync(r => r.Id == id);
            return View(round);
        }

        // ─── Helper: crear cola aleatoria ─────────────────────────────────────

        private static List<int> CreateShuffledQueue(int count)
        {
            List<int> indices = Enumerable.Range(0, count).ToList();
            Random rng = Random.Shared;
            for (int i = indices.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (indices[i], indices[j]) = (indices[j], indices[i]);
            }
            return indices;
        }
    }
}
