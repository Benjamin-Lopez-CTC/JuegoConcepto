namespace JuegoConcepto.Models
{
    public enum GamePhase
    {
        Setup,      // Ingresando jugadores
        Playing,    // Jugando
        Won,        // Ganado
        Lost        // Perdido
    }

    public class PlayerEntry
    {
        public string PlayerName { get; set; } = string.Empty;
        public string Color { get; set; } = string.Empty;
    }

    public class GameState
    {
        // Configuración inicial
        public List<string> Players { get; set; } = new();

        // Estado del juego
        public GamePhase Phase { get; set; } = GamePhase.Setup;
        public List<PlayerEntry> ColorHistory { get; set; } = new();

        // Ronda: índices de jugadores ya visitados en la ronda actual
        public List<int> RoundQueue { get; set; } = new();
        public int CurrentPlayerIndex { get; set; } = -1;

        // Tiempo (guardado en milisegundos)
        public long ElapsedMilliseconds { get; set; } = 0;
        public DateTime? StartTime { get; set; }

        // Resultado
        public string? WinningReason { get; set; }
        public string? DuplicateColor { get; set; }
        public int PlayersReached { get; set; } = 0;

        // Jugador actual
        public string? CurrentPlayerName =>
            (CurrentPlayerIndex >= 0 && CurrentPlayerIndex < Players.Count)
                ? Players[CurrentPlayerIndex]
                : null;

        // Total de jugadores que han ingresado color (completados)
        public int CompletedPlayers => ColorHistory.Count;
    }
}
