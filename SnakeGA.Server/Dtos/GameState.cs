using System.Text.Json.Serialization;

namespace SnakeGA.Server.Dtos;

public record class GameState(List<Point> Body, Point Food, int Health, NeuralNetwork Brain, int Points = 0, [property: JsonIgnore] Dictionary<Point, int> Visited = null!);