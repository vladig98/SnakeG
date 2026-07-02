namespace SnakeGA.Server.Dtos;

public class NeuralNetwork
{
    public int InputSize { get; } = 26;
    public int HiddenSize { get; } = 64;
    public int OutputSize { get; } = 4;

    public float[] Weights { get; set; } = [];
    public float[] Biases { get; set; } = [];

    public NeuralNetwork()
    {
        int totalWeights = (InputSize * HiddenSize) + (HiddenSize * OutputSize);
        Weights = new float[totalWeights];

        int totalBiases = HiddenSize + OutputSize;
        Biases = new float[totalBiases];
    }

    public void InitializeRandomWeights()
    {
        for (int i = 0; i < Weights.Length; i++)
        {
            Weights[i] = (float)(Random.Shared.NextDouble() * 2 - 1);
        }

        for (int i = 0; i < Biases.Length; i++)
        {
            Biases[i] = (float)(Random.Shared.NextDouble() * 2 - 1);
        }
    }

    /// <summary>
    /// Feed forward NN
    /// </summary>
    public float[] Predict(float[] inputs)
    {
        float[] hidden = new float[HiddenSize];
        float[] outputs = new float[OutputSize];

        int weightIndex = 0;
        int biasIndex = 0;

        for (int h = 0; h < HiddenSize; h++)
        {
            float sum = Biases[biasIndex];
            biasIndex++;

            for (int i = 0; i < InputSize; i++)
            {
                sum += inputs[i] * Weights[weightIndex];
                weightIndex++;
            }

            hidden[h] = MathF.Tanh(sum);
        }

        for (int o = 0; o < OutputSize; o++)
        {
            float sum = Biases[biasIndex];
            biasIndex++;

            for (int h = 0; h < HiddenSize; h++)
            {
                sum += hidden[h] * Weights[weightIndex];
                weightIndex++;
            }

            outputs[o] = MathF.Tanh(sum);
        }

        return outputs;
    }

    public NeuralNetwork CrossoverAndMutate(NeuralNetwork partner, float mutationRate = 0.05f)
    {
        NeuralNetwork baby = new();

        for (int i = 0; i < Weights.Length; i++)
        {
            baby.Weights[i] = Random.Shared.NextDouble() < 0.5 ? Weights[i] : partner.Weights[i];

            if (Random.Shared.NextDouble() < mutationRate)
            {
                // Random Reset Mutation
                if (Random.Shared.NextDouble() < 0.50)
                {
                    baby.Weights[i] = (float)(Random.Shared.NextDouble() * 2 - 1);
                }
                // Creep Mutation
                else
                {
                    float mutationAmount = (float)(Random.Shared.NextDouble() * 0.4 - 0.2);
                    baby.Weights[i] = Math.Clamp(baby.Weights[i] + mutationAmount, -1f, 1f);
                }
            }
        }

        for (int i = 0; i < Biases.Length; i++)
        {
            baby.Biases[i] = Random.Shared.NextDouble() < 0.5 ? Biases[i] : partner.Biases[i];

            if (Random.Shared.NextDouble() < mutationRate)
            {
                // Random Reset Mutation
                if (Random.Shared.NextDouble() < 0.50)
                {
                    baby.Biases[i] = (float)(Random.Shared.NextDouble() * 2 - 1);
                }
                // Creep Mutation
                else
                {
                    float mutationAmount = (float)(Random.Shared.NextDouble() * 0.4 - 0.2);
                    baby.Biases[i] = Math.Clamp(baby.Biases[i] + mutationAmount, -1f, 1f);
                }
            }
        }

        return baby;
    }
}