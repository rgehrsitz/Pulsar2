// File: Pulsar.Runtime/Rules/IRuleCoordinator.cs

namespace Pulsar.Runtime.Rules
{
    public interface IRuleCoordinator
    {
        void EvaluateRules(Dictionary<string, double> inputs, Dictionary<string, double> outputs);
    }
}