namespace SnakeGA.Server.Dtos;

public class NeuralNetwork
{
    public int InputSize { get; } = 24;
    public int HiddenSize { get; } = 16;
    public int OutputSize { get; } = 4;

    public float[] Weights { get; set; } = [];

    public NeuralNetwork()
    {
        int totalWeights = (InputSize * HiddenSize) + (HiddenSize * OutputSize);
        Weights = new float[totalWeights];
    }

    public void InitializeRandomWeights()
    {
        for (int i = 0; i < Weights.Length; i++)
        {
            Weights[i] = (float)(Random.Shared.NextDouble() * 2 - 1);
        }
    }

    public float[] Predict(float[] inputs)
    {
        float[] hidden = new float[HiddenSize];
        float[] outputs = new float[OutputSize];
        int weightIndex = 0;

        for (int h = 0; h < HiddenSize; h++)
        {
            float sum = 0;
            for (int i = 0; i < InputSize; i++)
            {
                sum += inputs[i] * Weights[weightIndex++];
            }

            hidden[h] = MathF.Tanh(sum);
        }

        for (int o = 0; o < OutputSize; o++)
        {
            float sum = 0;
            for (int h = 0; h < HiddenSize; h++)
            {
                sum += hidden[h] * Weights[weightIndex++];
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
                baby.Weights[i] = (float)(Random.Shared.NextDouble() * 2 - 1);
            }
        }

        return baby;
    }
}
