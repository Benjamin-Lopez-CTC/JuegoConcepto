using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace JuegoConcepto.Models
{
    // Las fases de una partida (mantenido del diseño anterior)
    public enum GamePhase
    {
        Setup,          // Configurando (en la Ronda, eligiendo jugadores)
        Playing,        // Partida en curso
        WonPendingId,   // Partida ganada, esperando ingresar el ID/Nombre del ganador para guardar
        Won,            // Ganada y guardada en leaderboard
        Lost            // Perdida
    }

    public class Round
    {
        [Key]
        public int Id { get; set; }
        public DateTime StartTime { get; set; } = DateTime.UtcNow;
        public DateTime? EndTime { get; set; }
        public bool IsActive { get; set; } = true;

        public virtual ICollection<Player> Players { get; set; } = new List<Player>();
        public virtual ICollection<Game> Games { get; set; } = new List<Game>();
    }

    public class Player
    {
        [Key]
        public int Id { get; set; }

        public int RoundId { get; set; }
        [ForeignKey(nameof(RoundId))]
        public virtual Round Round { get; set; } = null!;

        [Required]
        public string Name { get; set; } = string.Empty;
        
        // El orden en el que fue ingresado (útil para listarlos consistente)
        public int OrderIndex { get; set; }

        public virtual ICollection<ColorSubmission> ColorSubmissions { get; set; } = new List<ColorSubmission>();
    }

    public class Game
    {
        [Key]
        public int Id { get; set; }

        public int RoundId { get; set; }
        [ForeignKey(nameof(RoundId))]
        public virtual Round Round { get; set; } = null!;

        public GamePhase Phase { get; set; } = GamePhase.Playing;
        
        public DateTime StartTime { get; set; } = DateTime.UtcNow;
        public long ElapsedMilliseconds { get; set; } = 0;
        
        public string? WinningIdentifier { get; set; } // Nombre para el Leaderboard
        public string? WinningReason { get; set; }
        public string? DuplicateColor { get; set; }
        public int PlayersReached { get; set; } = 0;

        // Serializamos la "RoundQueue" de enteros como un JSON sencillo o comma-separated para saber a quién le toca
        public string? SerializedRoundQueue { get; set; }
        public int CurrentPlayerId { get; set; } // El FK del Player al que le toca ahora

        public virtual ICollection<ColorSubmission> ColorSubmissions { get; set; } = new List<ColorSubmission>();
    }

    public class ColorSubmission
    {
        [Key]
        public int Id { get; set; }

        public int GameId { get; set; }
        [ForeignKey(nameof(GameId))]
        public virtual Game Game { get; set; } = null!;

        public int PlayerId { get; set; }
        [ForeignKey(nameof(PlayerId))]
        public virtual Player Player { get; set; } = null!;

        [Required]
        public string Color { get; set; } = string.Empty;
        [Required]
        public string NormalizedColor { get; set; } = string.Empty;

        public DateTime InsertedAt { get; set; } = DateTime.UtcNow;
    }

    public class GameDbContext : DbContext
    {
        public GameDbContext(DbContextOptions<GameDbContext> options) : base(options) { }

        public DbSet<Round> Rounds { get; set; }
        public DbSet<Player> Players { get; set; }
        public DbSet<Game> Games { get; set; }
        public DbSet<ColorSubmission> ColorSubmissions { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            
            // Índices para búsquedas rápidas (por ejemplo: buscar un NormalizedColor en toda la ronda)
            modelBuilder.Entity<ColorSubmission>()
                .HasIndex(c => c.NormalizedColor);
        }
    }
}
