namespace SnakeGA.Server.Dtos;

public readonly record struct FastForwardDto
(
    int Generation,
    Dictionary<string, GameState?> Players
);
