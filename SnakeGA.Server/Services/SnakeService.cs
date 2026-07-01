using Microsoft.AspNetCore.SignalR;
using SnakeGA.Server.Dtos;
using SnakeGA.Server.Hubs;

namespace SnakeGA.Server.Services;

public class SnakeService(IHubContext<SnakeHub> hubContext) : BackgroundService
{
    private NeuralNetwork? _bestPreviousBrain = null;
    private readonly Dictionary<string, GameState?> _gameStates = [];
    private readonly List<GameState> _finalStates = [];
    private readonly Dictionary<string, bool> _gameOver = [];
    private const int populationSize = 100;
    private const int width = 60;
    private const int heigth = 30;
    private const int initialSize = 5;
    private const int tournamentSize = 5;
    private const int numberOfParents = 10;
    private const string best = "best";
    private int gen = 1;
    private const int eatenApplePoints = 100;
    private const int wrongDirectionPoints = -3;
    private const int rightDirectionPoints = 1;
    private const int pointForLooping = -100;
    private const int preTrainGenerations = 5_000;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        while (gen <= preTrainGenerations && !stoppingToken.IsCancellationRequested)
        {
            if (gen % 100 == 0)
            {
                Console.WriteLine($"Gen: {gen}");
            }
            GenerateNextGenerationData();
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var gameStates = GenerateNextGenerationData();
            var payload = new { Generation = gen, Players = gameStates };

            await hubContext.Clients.All.SendAsync("ReceiveTick", payload, stoppingToken);
            await Task.Delay(100, stoppingToken);
        }
    }

    private Dictionary<string, GameState?> GenerateNextGenerationData()
    {
        bool allDead = true;
        for (int i = 0; i < populationSize; i++)
        {
            string key = i.ToString();

            if (!_gameOver.TryGetValue(key, out bool isGameOver))
            {
                _gameOver.Add(key, false);
            }

            if (isGameOver)
            {
                continue;
            }

            if (!_gameStates.TryGetValue(key, out GameState? state))
            {
                _gameStates.Add(key, null);
            }

            state = UpdateBody(state);
            if (state == null)
            {
                _gameOver[key] = true;
                _gameStates[key] = null;

                continue;
            }

            state = UpdateFood(state);
            _gameStates[key] = state;
            allDead = false;
        }

        if (_bestPreviousBrain != null)
        {
            if (!_gameOver.TryGetValue(best, out bool isBestDead))
            {
                _gameOver.Add(best, false);
            }

            if (!isBestDead)
            {
                if (!_gameStates.TryGetValue(best, out GameState? bestState) || bestState == null)
                {
                    bestState = new GameState(
                        Body: [],
                        Food: new Point(-1, -1),
                        Health: width * heigth,
                        Brain: _bestPreviousBrain,
                        Visited: []
                    );
                }

                bestState = UpdateBody(bestState);
                if (bestState == null)
                {
                    _gameOver[best] = true;
                    _gameStates[best] = null;
                }
                else
                {
                    bestState = UpdateFood(bestState);
                    _gameStates[best] = bestState;
                }
            }
        }
        else
        {
            _gameStates[best] = null;
        }

        if (allDead)
        {
            List<GameState> states = _finalStates;

            GameState champion = states.OrderByDescending(s => s.Points).First();
            _bestPreviousBrain = champion.Brain;

            List<GameState> parents = [];
            for (int i = 0; i < numberOfParents; i++)
            {
                GameState? bestContender = null;
                for (int t = 0; t < tournamentSize; t++)
                {
                    int randomIndex = Random.Shared.Next(states.Count);
                    GameState contender = states[randomIndex];

                    if (bestContender == null || contender.Points > bestContender.Points)
                    {
                        bestContender = contender;
                    }
                }

                parents.Add(bestContender!);
            }

            List<NeuralNetwork> nextGenerationBrains = [];
            for (int i = 0; i < populationSize; i++)
            {
                GameState parentA = parents[Random.Shared.Next(parents.Count)];
                GameState parentB = parents[Random.Shared.Next(parents.Count)];

                NeuralNetwork babyBrain = parentA.Brain.CrossoverAndMutate(parentB.Brain, mutationRate: 0.05f);
                nextGenerationBrains.Add(babyBrain);
            }

            _finalStates.Clear();
            for (int i = 0; i < populationSize; i++)
            {
                string key = i.ToString();
                _gameOver[key] = false;

                _gameStates[key] = new GameState(
                    Body: [],
                    Food: new Point(-1, -1),
                    Health: width * heigth,
                    Brain: nextGenerationBrains[i],
                    Visited: []
                );
            }

            _gameOver[best] = false;
            _gameStates[best] = null;

            gen++;
        }

        return _gameStates;
    }

    private GameState UpdateFood(GameState? state)
    {
        state ??= GetGameState();

        Point food = state.Food;
        if (food is { X: -1, Y: -1 })
        {
            food = PickLocation(state.Body);
            state = state with { Food = food };
        }

        return state;
    }

    private static GameState GetGameState()
    {
        NeuralNetwork freshBrain = new();
        freshBrain.InitializeRandomWeights();

        return new GameState(
            Body: [],
            Food: new Point(-1, -1),
            Health: width * heigth,
            Brain: freshBrain,
            Visited: []
        );
    }

    private Point PickLocation(List<Point> body)
    {
        HashSet<Point> taken = [.. body];
        List<Point> empty = [];

        for (int i = 0; i < width; i++)
        {
            for (int j = 0; j < heigth; j++)
            {
                Point p = new(i, j);
                if (taken.Contains(p))
                {
                    continue;
                }

                empty.Add(p);
            }
        }

        int index = Random.Shared.Next(empty.Count);
        return empty[index];
    }

    private GameState? UpdateBody(GameState? state)
    {
        int maxHealth = width * heigth;

        state ??= GetGameState();
        List<Point> body = state.Body;

        if (body is not { Count: > 0 })
        {
            for (int i = 0; i < initialSize; i++)
            {
                body.Add(new Point(i, 0));
            }

            state = state with { Body = body };
        }

        Point head = body[^1];
        int initialDistance = GetDistance(head, state.Food);

        float[] vision = GetVision(head, body, state.Food);
        float[] decisions = state.Brain.Predict(vision);

        int dir = 0;
        float maxDecisionValue = decisions[0];
        for (int i = 1; i < 4; i++)
        {
            if (decisions[i] > maxDecisionValue)
            {
                maxDecisionValue = decisions[i];
                dir = i;
            }
        }

        Point newHead = head;
        switch (dir)
        {
            case 0: newHead = head with { X = head.X + 1 }; break; // Right
            case 1: newHead = head with { X = head.X - 1 }; break; // Left
            case 2: newHead = head with { Y = head.Y + 1 }; break; // Down
            case 3: newHead = head with { Y = head.Y - 1 }; break; // Up
        }

        if (!state.Visited.TryGetValue(newHead, out int visitCount))
        {
            visitCount = 0;
        }
        visitCount++;
        state.Visited[newHead] = visitCount;

        int pointsModifier = 0;
        if (visitCount == 3)
        {
            pointsModifier += pointForLooping;
            _finalStates.Add(state with { Points = state.Points + pointsModifier });
            return null;
        }

        if (newHead.X < 0 || newHead.X >= width || newHead.Y < 0 || newHead.Y >= heigth)
        {
            _finalStates.Add(state with { Points = state.Points + pointsModifier });
            return null;
        }

        if (body.Contains(newHead) && newHead != body[0])
        {
            _finalStates.Add(state with { Points = state.Points + pointsModifier });
            return null;
        }

        int currentHealth = state.Health - 1;
        if (currentHealth <= 0)
        {
            _finalStates.Add(state with { Points = state.Points + pointsModifier });

            return null;
        }

        body.Add(newHead);
        if (newHead.X == state.Food.X && newHead.Y == state.Food.Y)
        {
            state = state with { Food = new Point(-1, -1), Health = maxHealth, Points = state.Points + eatenApplePoints + pointsModifier, Visited = [] };
        }
        else
        {
            body.RemoveAt(0);
            if (state.Food.X != -1)
            {
                int finalDistance = GetDistance(newHead, state.Food);
                if (finalDistance < initialDistance)
                {
                    pointsModifier += rightDirectionPoints;
                }
                else
                {
                    pointsModifier += wrongDirectionPoints;
                }
            }

            state = state with { Health = currentHealth, Points = state.Points + pointsModifier };
        }

        return state;
    }
    private static float[] GetVision(Point head, List<Point> body, Point food)
    {
        float[] vision = new float[26];
        HashSet<Point> bodySet = [.. body];

        int[] dx = [0, 1, 1, 1, 0, -1, -1, -1];
        int[] dy = [-1, -1, 0, 1, 1, 1, 0, -1];

        for (int i = 0; i < 8; i++)
        {
            int x = head.X;
            int y = head.Y;
            float distance = 0;

            bool foundFood = false;
            bool foundTail = false;

            float distanceToFood = 0;
            float distanceToTail = 0;

            while (true)
            {
                x += dx[i];
                y += dy[i];
                distance++;

                if (x < 0 || x >= width || y < 0 || y >= heigth)
                {
                    int index = i * 3;
                    vision[index] = 1.0f / distance;
                    vision[index + 1] = foundFood ? 1.0f / distanceToFood : 0;
                    vision[index + 2] = foundTail ? 1.0f / distanceToTail : 0;
                    break;
                }

                if (!foundFood && x == food.X && y == food.Y)
                {
                    foundFood = true;
                    distanceToFood = distance;
                }

                if (!foundTail && bodySet.Contains(new Point(x, y)))
                {
                    foundTail = true;
                    distanceToTail = distance;
                }
            }
        }

        vision[24] = (float)(food.X - head.X) / width;
        vision[25] = (float)(food.Y - head.Y) / heigth;

        return vision;
    }

    private static int GetDistance(Point head, Point food)
    {
        return Math.Abs(head.X - food.X) + Math.Abs(head.Y - food.Y);
    }
}