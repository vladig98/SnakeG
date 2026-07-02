namespace SnakeGA.Server.Dtos;

public readonly record struct ShortestPathDto
(
    Point Position,
    int Distance
);
