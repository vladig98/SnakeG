namespace SnakeGA.Server.Dtos;

public class SimulationControl
{
    public int TargetGeneration { get; set; } = 1;

    // Genetic Hyperparameters
    public float MutationRate { get; set; } = 0.05f;
    public int TournamentSize { get; set; } = 5;
    public int ElitismCount { get; set; } = 3;
    public int NumberOfParents { get; set; } = 10;

    // Scoring & Environment
    public int EatenApplePoints { get; set; } = 10_000;
    public int ExtraApplesMultiplier { get; set; } = 5000;
    public int RightDirectionPoints { get; set; } = 1;
    public int WrongDirectionPoints { get; set; } = 0;
    public int PointForLooping { get; set; } = -100;
    public int DeathPenalty { get; set; } = -10_000;
    public int NumberOfRepeats { get; set; } = 3;
    public int HealthOffset { get; set; } = 50;

    public NeuralNetwork? BestBrain { get; set; }
    public NeuralNetwork? InjectedBrain { get; set; }
}
