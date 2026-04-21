using Microsoft.AspNetCore.Mvc;
using JuegoConcepto.Models;
using System.Text.Json;

namespace JuegoConcepto.Controllers
{
    public class HomeController : Controller
    {
        private const string SessionKey = "GameState";

        // ─── Helpers ──────────────────────────────────────────────────────────

        private GameState GetState()
        {
            string? json = HttpContext.Session.GetString(SessionKey);
            if (string.IsNullOrEmpty(json))
                return new GameState();
            return JsonSerializer.Deserialize<GameState>(json) ?? new GameState();
        }

        private void SaveState(GameState state)
        {
            HttpContext.Session.SetString(SessionKey, JsonSerializer.Serialize(state));
        }

        // Normalizar color: minúsculas, sin espacios extra, sin tildes comunes
        private static string NormalizeColor(string color)
        {
            if (string.IsNullOrWhiteSpace(color)) return string.Empty;

            string normalized = color.Trim().ToLowerInvariant();

            // Reemplazar vocales con tilde
            normalized = normalized
                .Replace('á', 'a').Replace('é', 'e').Replace('í', 'i')
                .Replace('ó', 'o').Replace('ú', 'u').Replace('ü', 'u');

            return normalized;
        }

        // ─── GET / ────────────────────────────────────────────────────────────

        [HttpGet]
        public IActionResult Index()
        {
            GameState state = GetState();
            return View(state);
        }

        // ─── POST /Home/AddPlayer ─────────────────────────────────────────────

        [HttpPost]
        public IActionResult AddPlayer(string playerName)
        {
            GameState state = GetState();

            if (state.Phase != GamePhase.Setup)
                return RedirectToAction(nameof(Index));

            string name = playerName?.Trim() ?? string.Empty;

            if (!string.IsNullOrEmpty(name) && !state.Players.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                state.Players.Add(name);
                SaveState(state);
            }

            return RedirectToAction(nameof(Index));
        }

        // ─── POST /Home/RemovePlayer ──────────────────────────────────────────

        [HttpPost]
        public IActionResult RemovePlayer(int index)
        {
            GameState state = GetState();

            if (state.Phase != GamePhase.Setup)
                return RedirectToAction(nameof(Index));

            if (index >= 0 && index < state.Players.Count)
            {
                state.Players.RemoveAt(index);
                SaveState(state);
            }

            return RedirectToAction(nameof(Index));
        }

        // ─── POST /Home/StartGame ─────────────────────────────────────────────

        [HttpPost]
        public IActionResult StartGame()
        {
            GameState state = GetState();

            if (state.Phase != GamePhase.Setup || state.Players.Count < 2)
                return RedirectToAction(nameof(Index));

            // Preparar la primera ronda: shuffle de índices
            state.Phase = GamePhase.Playing;
            state.ColorHistory.Clear();
            state.StartTime = DateTime.UtcNow;
            state.ElapsedMilliseconds = 0;
            state.PlayersReached = 0;

            // Crear cola aleatoria con todos los jugadores
            state.RoundQueue = CreateShuffledQueue(state.Players.Count);
            state.CurrentPlayerIndex = state.RoundQueue[0];
            state.RoundQueue.RemoveAt(0);

            SaveState(state);
            return RedirectToAction(nameof(Index));
        }

        // ─── POST /Home/SubmitColor ───────────────────────────────────────────

        [HttpPost]
        public IActionResult SubmitColor(string color, long elapsedMs)
        {
            GameState state = GetState();

            if (state.Phase != GamePhase.Playing)
                return RedirectToAction(nameof(Index));

            string normalized = NormalizeColor(color);

            if (string.IsNullOrEmpty(normalized))
                return RedirectToAction(nameof(Index));

            // Guardar tiempo recibido del cliente
            state.ElapsedMilliseconds = elapsedMs;

            // Verificar si el color se repite
            string? duplicate = state.ColorHistory
                .FirstOrDefault(e => NormalizeColor(e.Color) == normalized)?.Color;

            if (duplicate != null)
            {
                // Perdieron
                state.Phase = GamePhase.Lost;
                state.DuplicateColor = duplicate;
                state.PlayersReached = state.ColorHistory.Count + 1; // incluyendo el actual
                state.WinningReason = $"El color \"{color}\" ya fue dicho antes.";
            }
            else
            {
                // Agregar a historial
                state.ColorHistory.Add(new PlayerEntry
                {
                    PlayerName = state.CurrentPlayerName ?? string.Empty,
                    Color = color
                });

                state.PlayersReached = state.ColorHistory.Count;

                // ¿Se completaron todos los jugadores en la ronda?
                if (state.RoundQueue.Count == 0 && state.ColorHistory.Count >= state.Players.Count)
                {
                    // ¡Ganaron!
                    state.Phase = GamePhase.Won;
                    state.WinningReason = "¡Completaron una ronda sin repetir colores!";
                }
                else
                {
                    // Avanzar al siguiente jugador
                    if (state.RoundQueue.Count == 0)
                    {
                        // Empezar nueva ronda
                        state.RoundQueue = CreateShuffledQueue(state.Players.Count);
                    }

                    state.CurrentPlayerIndex = state.RoundQueue[0];
                    state.RoundQueue.RemoveAt(0);
                }
            }

            SaveState(state);
            return RedirectToAction(nameof(Index));
        }

        // ─── POST /Home/ResetGame ─────────────────────────────────────────────

        [HttpPost]
        public IActionResult ResetGame()
        {
            GameState state = GetState();

            // Mantener lista de jugadores, volver a Setup
            List<string> players = new(state.Players);
            state = new GameState
            {
                Players = players,
                Phase = GamePhase.Setup
            };

            SaveState(state);
            return RedirectToAction(nameof(Index));
        }

        // ─── POST /Home/FullReset ─────────────────────────────────────────────

        [HttpPost]
        public IActionResult FullReset()
        {
            HttpContext.Session.Remove(SessionKey);
            return RedirectToAction(nameof(Index));
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
