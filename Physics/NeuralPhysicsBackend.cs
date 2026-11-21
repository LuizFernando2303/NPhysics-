using OpenTK.Mathematics;

namespace NPhysics;

internal sealed class NeuralPhysicsBackend : IDisposable
{
    private const int MaximumNeighbors = DatasetSchema.MaxNeighborCount;
    private const float MaximumPositionCorrection = 0.25f;
    private const float MaximumVelocityCorrection = 10f;
    private const float MaximumAngularCorrection = 50f;

    private readonly ModelSerializer.ModelParameters _model;
    private readonly GpuModelWeights _weights;
    private readonly GpuGraphInference _inference;

    public NeuralPhysicsBackend(string modelPath)
    {
        _model = ModelSerializer.Load(modelPath);
        _weights = new GpuModelWeights(_model);
        _inference = new GpuGraphInference(_weights);
        ModelPath = Path.GetFullPath(modelPath);
        Console.WriteLine($"Backend neural experimental: {ModelPath}");
        Console.WriteLine($"Historico do modelo: {_model.TrainingHistory}");
        Console.WriteLine("Interacoes entre corpos sao limitadas aos 16 vizinhos previstos pelo modelo.");
    }

    public string ModelPath { get; }

    public void Step(
        ProceduralTrack track,
        float[] positionInput,
        float[] velocityInput,
        float[] spinInput,
        float[] materials,
        float[] positionOutput,
        float[] velocityOutput,
        float[] spinOutput,
        float deltaTime)
    {
        var nodeCount = positionInput.Length / 4;
        var nodeInputs = new float[nodeCount * GraphNetwork.NodeInputSize];
        var edgeCounts = new int[nodeCount];
        var edgeTargets = new List<int>(nodeCount * 8);
        var edgeFeatures = new List<float>(nodeCount * 8 * GraphNetwork.EdgeInputSize);
        var cells = BuildCellMap(positionInput, nodeCount);
        var freePositions = new Vector3[nodeCount];
        var freeVelocities = new Vector3[nodeCount];

        for (var node = 0; node < nodeCount; node++)
        {
            PrepareNode(
                node,
                track,
                positionInput,
                velocityInput,
                spinInput,
                materials,
                nodeInputs,
                freePositions,
                freeVelocities,
                deltaTime);
            PrepareNeighbors(
                node,
                positionInput,
                velocityInput,
                spinInput,
                materials,
                cells,
                nodeInputs,
                freePositions[node],
                freeVelocities[node],
                edgeCounts,
                edgeTargets,
                edgeFeatures);
            var inputOffset = node * GraphNetwork.NodeInputSize;
            nodeInputs[inputOffset + 37] = edgeCounts[node];
        }

        Normalize(nodeInputs, _model.NodeMeans, _model.NodeStandardDeviations);
        var rawOutput = _inference.Forward(
            nodeInputs,
            edgeFeatures.ToArray(),
            edgeCounts,
            edgeTargets.ToArray());
        Denormalize(rawOutput, _model.TargetMeans, _model.TargetStandardDeviations);
        WriteState(
            nodeCount,
            positionInput,
            spinInput,
            positionOutput,
            velocityOutput,
            spinOutput,
            freePositions,
            freeVelocities,
            rawOutput,
            deltaTime);
    }

    private static Dictionary<Cell, List<int>> BuildCellMap(float[] positions, int nodeCount)
    {
        var cells = new Dictionary<Cell, List<int>>(nodeCount);
        for (var node = 0; node < nodeCount; node++)
        {
            var offset = node * 4;
            var cell = GetCell(positions[offset], positions[offset + 1], positions[offset + 2]);
            if (!cells.TryGetValue(cell, out var occupants))
            {
                occupants = [];
                cells.Add(cell, occupants);
            }
            occupants.Add(node);
        }
        return cells;
    }

    private static void PrepareNode(
        int node,
        ProceduralTrack track,
        float[] positions,
        float[] velocities,
        float[] spins,
        float[] materials,
        float[] nodeInputs,
        Vector3[] freePositions,
        Vector3[] freeVelocities,
        float deltaTime)
    {
        var stateOffset = node * 4;
        var spinOffset = node * 8;
        var inputOffset = node * GraphNetwork.NodeInputSize;
        var radius = positions[stateOffset + 3];
        var mass = materials[stateOffset];
        var inverseMass = 1f / mass;
        var angular = new Vector3(spins[spinOffset + 4], spins[spinOffset + 5], spins[spinOffset + 6]);
        var velocity = new Vector3(
            velocities[stateOffset],
            velocities[stateOffset + 1],
            velocities[stateOffset + 2]);
        var freeVelocity = velocity + new Vector3(0f, -SimulationSettings.Gravity, 0f) * deltaTime;
        var speed = freeVelocity.Length;
        if (speed > 0.00001f)
        {
            var area = MathF.PI * radius * radius;
            var drag = -0.5f * SimulationSettings.AirDensity
                * SimulationSettings.AirDragCoefficient * area * speed * inverseMass;
            freeVelocity += freeVelocity * (drag * deltaTime);
        }

        var position = new Vector3(
            positions[stateOffset],
            positions[stateOffset + 1],
            positions[stateOffset + 2]);
        var freePosition = position + freeVelocity * deltaTime;
        var slopeX = (track.Height(freePosition.X + 0.01f, freePosition.Z)
            - track.Height(freePosition.X - 0.01f, freePosition.Z)) / 0.02f;
        var slopeZ = (track.Height(freePosition.X, freePosition.Z + 0.01f)
            - track.Height(freePosition.X, freePosition.Z - 0.01f)) / 0.02f;
        var rampNormal = new Vector3(-slopeX, 1f, -slopeZ).Normalized();
        var rampSurface = new Vector3(
            freePosition.X,
            track.Height(freePosition.X, freePosition.Z),
            freePosition.Z);
        var signedDistance = Vector3.Dot(freePosition - rampSurface, rampNormal);
        var contactOffset = -rampNormal * radius;
        var rampContactVelocity = freeVelocity + Vector3.Cross(angular, contactOffset);
        var rampNormalSpeed = Vector3.Dot(rampContactVelocity, rampNormal);
        var tangentSpeed = (rampContactVelocity - rampNormal * rampNormalSpeed).Length;

        freePositions[node] = freePosition;
        freeVelocities[node] = freeVelocity;

        nodeInputs[inputOffset] = position.X;
        nodeInputs[inputOffset + 1] = position.Y;
        nodeInputs[inputOffset + 2] = position.Z;
        nodeInputs[inputOffset + 3] = radius;
        nodeInputs[inputOffset + 4] = velocity.X;
        nodeInputs[inputOffset + 5] = velocity.Y;
        nodeInputs[inputOffset + 6] = velocity.Z;
        nodeInputs[inputOffset + 7] = mass;
        Array.Copy(spins, spinOffset, nodeInputs, inputOffset + 8, 4);
        nodeInputs[inputOffset + 12] = angular.X;
        nodeInputs[inputOffset + 13] = angular.Y;
        nodeInputs[inputOffset + 14] = angular.Z;
        nodeInputs[inputOffset + 15] =
            SimulationSettings.SolidSphereInertiaFactor * mass * radius * radius;
        nodeInputs[inputOffset + 16] = freePosition.X;
        nodeInputs[inputOffset + 17] = freePosition.Y;
        nodeInputs[inputOffset + 18] = freePosition.Z;
        nodeInputs[inputOffset + 19] = inverseMass;
        nodeInputs[inputOffset + 20] = freeVelocity.X;
        nodeInputs[inputOffset + 21] = freeVelocity.Y;
        nodeInputs[inputOffset + 22] = freeVelocity.Z;
        nodeInputs[inputOffset + 23] = signedDistance;
        nodeInputs[inputOffset + 24] = rampNormal.X;
        nodeInputs[inputOffset + 25] = rampNormal.Y;
        nodeInputs[inputOffset + 26] = rampNormal.Z;
        nodeInputs[inputOffset + 27] = materials[stateOffset + 1];
        nodeInputs[inputOffset + 28] = materials[stateOffset + 2];
        nodeInputs[inputOffset + 29] = materials[stateOffset + 3];
        nodeInputs[inputOffset + 30] = -SimulationSettings.Gravity;
        nodeInputs[inputOffset + 31] = deltaTime;
        nodeInputs[inputOffset + 32] = slopeX;
        nodeInputs[inputOffset + 33] = slopeZ;
        nodeInputs[inputOffset + 34] = rampNormalSpeed;
        nodeInputs[inputOffset + 35] = tangentSpeed;
        nodeInputs[inputOffset + 39] = signedDistance < radius + 0.002f ? 1f : 0f;
    }

    private static void PrepareNeighbors(
        int node,
        float[] positions,
        float[] velocities,
        float[] spins,
        float[] materials,
        Dictionary<Cell, List<int>> cells,
        float[] nodeInputs,
        Vector3 freePosition,
        Vector3 freeVelocity,
        int[] edgeCounts,
        List<int> edgeTargets,
        List<float> edgeFeatures)
    {
        var radius = positions[node * 4 + 3];
        var baseCell = GetCell(freePosition.X, freePosition.Y, freePosition.Z);
        var candidates = new List<NeighborFeature>(MaximumNeighbors + 1);
        var hasSphereContact = false;
        var contactCount = 0;
        var overflow = false;
        var highestRelativeSpeed = 0f;
        var angularBefore = new Vector3(
            spins[node * 8 + 4],
            spins[node * 8 + 5],
            spins[node * 8 + 6]);
        for (var z = -1; z <= 1; z++)
        for (var y = -1; y <= 1; y++)
        for (var x = -1; x <= 1; x++)
        {
            var candidateCell = new Cell(baseCell.X + x, baseCell.Y + y, baseCell.Z + z);
            if (!cells.TryGetValue(candidateCell, out var occupants))
            {
                continue;
            }

            foreach (var other in occupants)
            {
                if (other == node)
                {
                    continue;
                }

                var otherOffset = other * 4;
                var otherCell = GetCell(
                    positions[otherOffset],
                    positions[otherOffset + 1],
                    positions[otherOffset + 2]);
                if (otherCell != candidateCell)
                {
                    continue;
                }

                var otherPosition = new Vector3(
                    positions[otherOffset],
                    positions[otherOffset + 1],
                    positions[otherOffset + 2]);
                var relativePosition = otherPosition - freePosition;
                var centerDistance = relativePosition.Length;
                var otherRadius = positions[otherOffset + 3];
                var contactDistance = radius + otherRadius;
                var penetration = contactDistance - centerDistance;
                if (penetration > 0f)
                {
                    hasSphereContact = true;
                    contactCount++;
                }

                var otherVelocity = new Vector3(
                    velocities[otherOffset],
                    velocities[otherOffset + 1],
                    velocities[otherOffset + 2]);
                var relativeVelocity = otherVelocity - freeVelocity;
                var otherAngular = new Vector3(
                    spins[other * 8 + 4],
                    spins[other * 8 + 5],
                    spins[other * 8 + 6]);
                var relativeAngular = otherAngular - angularBefore;
                var normal = centerDistance > 0.000001f
                    ? -relativePosition / centerDistance
                    : Vector3.UnitX;
                var relativeContactVelocity = freeVelocity
                    + Vector3.Cross(angularBefore, -normal * radius)
                    - otherVelocity
                    - Vector3.Cross(otherAngular, normal * otherRadius);
                var relativeSpeed = relativeContactVelocity.Length;
                highestRelativeSpeed = MathF.Max(highestRelativeSpeed, relativeSpeed);
                if (centerDistance > contactDistance + 0.05f)
                {
                    continue;
                }
                if (candidates.Count >= MaximumNeighbors)
                {
                    overflow = true;
                    continue;
                }

                var feature = new float[GraphNetwork.EdgeInputSize];
                feature[0] = relativePosition.X;
                feature[1] = relativePosition.Y;
                feature[2] = relativePosition.Z;
                feature[3] = centerDistance;
                feature[4] = relativeVelocity.X;
                feature[5] = relativeVelocity.Y;
                feature[6] = relativeVelocity.Z;
                feature[7] = relativeAngular.X;
                feature[8] = relativeAngular.Y;
                feature[9] = relativeAngular.Z;
                feature[10] = penetration;
                feature[11] = otherRadius;
                feature[12] = materials[otherOffset];
                feature[13] = materials[otherOffset + 1];
                feature[14] = materials[otherOffset + 2];
                feature[15] = materials[otherOffset + 3];
                feature[16] = relativeContactVelocity.X;
                feature[17] = relativeContactVelocity.Y;
                feature[18] = relativeContactVelocity.Z;
                feature[19] = radius + otherRadius;
                candidates.Add(new NeighborFeature(other, feature));
            }
        }

        edgeCounts[node] = candidates.Count;
        foreach (var neighbor in candidates)
        {
            edgeTargets.Add(neighbor.Target);
            edgeFeatures.AddRange(neighbor.Features);
        }

        var inputOffset = node * GraphNetwork.NodeInputSize;
        var hasRampContact = nodeInputs[inputOffset + 39] > 0f;
        nodeInputs[inputOffset + 36] = highestRelativeSpeed > 1.5f
            ? 4f
            : hasSphereContact
                ? (hasRampContact ? 3f : 2f)
                : hasRampContact
                    ? 1f
                    : 0f;
        nodeInputs[inputOffset + 37] = candidates.Count;
        nodeInputs[inputOffset + 38] = overflow ? 1f : 0f;
        nodeInputs[inputOffset + 40] = hasSphereContact ? 1f : 0f;
        nodeInputs[inputOffset + 41] = highestRelativeSpeed;
    }

    private static void WriteState(
        int nodeCount,
        float[] positionInput,
        float[] spinInput,
        float[] positionOutput,
        float[] velocityOutput,
        float[] spinOutput,
        Vector3[] freePositions,
        Vector3[] freeVelocities,
        float[] corrections,
        float deltaTime)
    {
        for (var node = 0; node < nodeCount; node++)
        {
            var stateOffset = node * 4;
            var spinOffset = node * 8;
            var correctionOffset = node * GraphNetwork.OutputSize;
            var positionCorrection = new Vector3(
                Math.Clamp(corrections[correctionOffset], -MaximumPositionCorrection, MaximumPositionCorrection),
                Math.Clamp(corrections[correctionOffset + 1], -MaximumPositionCorrection, MaximumPositionCorrection),
                Math.Clamp(corrections[correctionOffset + 2], -MaximumPositionCorrection, MaximumPositionCorrection));
            var velocityCorrection = new Vector3(
                Math.Clamp(corrections[correctionOffset + 3], -MaximumVelocityCorrection, MaximumVelocityCorrection),
                Math.Clamp(corrections[correctionOffset + 4], -MaximumVelocityCorrection, MaximumVelocityCorrection),
                Math.Clamp(corrections[correctionOffset + 5], -MaximumVelocityCorrection, MaximumVelocityCorrection));
            var previousAngular = new Vector3(
                spinInput[spinOffset + 4],
                spinInput[spinOffset + 5],
                spinInput[spinOffset + 6]);
            var angularCorrection = new Vector3(
                Math.Clamp(corrections[correctionOffset + 6], -MaximumAngularCorrection, MaximumAngularCorrection),
                Math.Clamp(corrections[correctionOffset + 7], -MaximumAngularCorrection, MaximumAngularCorrection),
                Math.Clamp(corrections[correctionOffset + 8], -MaximumAngularCorrection, MaximumAngularCorrection));
            var position = freePositions[node] + positionCorrection;
            var velocity = freeVelocities[node] + velocityCorrection;
            var angular = previousAngular + angularCorrection;
            if (!float.IsFinite(position.X)
                || !float.IsFinite(position.Y)
                || !float.IsFinite(position.Z))
            {
                position = freePositions[node];
            }
            if (!float.IsFinite(velocity.X)
                || !float.IsFinite(velocity.Y)
                || !float.IsFinite(velocity.Z))
            {
                velocity = freeVelocities[node];
            }
            if (!float.IsFinite(angular.X)
                || !float.IsFinite(angular.Y)
                || !float.IsFinite(angular.Z))
            {
                angular = previousAngular;
            }
            positionOutput[stateOffset] = position.X;
            positionOutput[stateOffset + 1] = position.Y;
            positionOutput[stateOffset + 2] = position.Z;
            positionOutput[stateOffset + 3] = positionInput[stateOffset + 3];
            velocityOutput[stateOffset] = velocity.X;
            velocityOutput[stateOffset + 1] = velocity.Y;
            velocityOutput[stateOffset + 2] = velocity.Z;
            velocityOutput[stateOffset + 3] = 0f;
            IntegrateOrientation(spinInput, spinOutput, spinOffset, angular, deltaTime);
        }
    }

    private static void IntegrateOrientation(
        float[] spinInput,
        float[] spinOutput,
        int offset,
        Vector3 angular,
        float deltaTime)
    {
        var orientation = new Vector4(
            spinInput[offset],
            spinInput[offset + 1],
            spinInput[offset + 2],
            spinInput[offset + 3]);
        orientation = RigidBodyNumerics.IntegrateOrientation(
            orientation,
            angular,
            deltaTime);
        spinOutput[offset] = orientation.X;
        spinOutput[offset + 1] = orientation.Y;
        spinOutput[offset + 2] = orientation.Z;
        spinOutput[offset + 3] = orientation.W;
        spinOutput[offset + 4] = angular.X;
        spinOutput[offset + 5] = angular.Y;
        spinOutput[offset + 6] = angular.Z;
        spinOutput[offset + 7] = 0f;
    }

    private static void Normalize(float[] values, float[] means, float[] deviations)
    {
        var width = means.Length;
        for (var offset = 0; offset < values.Length; offset += width)
        {
            for (var feature = 0; feature < width; feature++)
            {
                values[offset + feature] = (values[offset + feature] - means[feature]) / deviations[feature];
            }
        }
    }

    private static void Denormalize(float[] values, float[] means, float[] deviations)
    {
        for (var offset = 0; offset < values.Length; offset += means.Length)
        {
            for (var feature = 0; feature < means.Length; feature++)
            {
                values[offset + feature] = values[offset + feature] * deviations[feature] + means[feature];
            }
        }
    }

    private static Cell GetCell(float x, float y, float z)
    {
        return new Cell(
            (int)MathF.Floor(x / SimulationSettings.GridCellSize),
            (int)MathF.Floor(y / SimulationSettings.GridCellSize),
            (int)MathF.Floor(z / SimulationSettings.GridCellSize));
    }

    public void Dispose()
    {
        _inference.Dispose();
        _weights.Dispose();
    }

    private readonly record struct Cell(int X, int Y, int Z);
    private readonly record struct NeighborFeature(int Target, float[] Features);
}
