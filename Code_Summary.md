# Code Summary

## Environment and Procedural Generation

The training environment is built within the Unity engine, utilizing the ML-Agents toolkit to facilitate reinforcement learning. To ensure the agent learns generalized navigation policies rather than memorizing specific map layouts, the environment employs a robust procedural generation system adapted from the "Daedalus Dungeon Generator" by Dillon Drummond. This system generates dungeon-like structures using a multi-stage algorithm:

1. **Room Placement:** Rooms of randomized dimensions (defined by `minSize` and `maxSize` vectors) are placed stochastically within a defined grid volume. The system attempts to place `numRandomRooms` (defaulting to 10) in non-overlapping positions.
2. **Graph Generation (Delaunay Tetrahedralization):** The centers of the placed rooms are used as vertices for a Delaunay Tetrahedralization process. This algorithm, implemented in `DelaunayTetrahedralization.cs`, calculates circumspheres for potential tetrahedrons to determine valid connections. This results in a fully connected graph (a "super-graph") where every room is connected to its neighbors, ensuring no isolated sub-graphs exist.
3. **Topology Optimization (Minimum Spanning Tree):** To reduce the connectivity to a more maze-like structure, a Minimum Spanning Tree (MST) is derived from the Delaunay graph using Prim's algorithm (implemented in `MinimumSpanningTree.cs`). This step prunes redundant edges, leaving a minimal set of essential connections that ensure all rooms are reachable without creating trivial cycles. This creates the "backbone" of the dungeon.
4. **Hallway Generation:** The edges of the MST are converted into physical hallways. An A\* pathfinding algorithm runs on the grid cells (`AStar.cs`) to find the shortest path between the connected rooms. These paths are then "carved" out of the grid, marking cells as `HALLWAY` types.
5. **Wall Generation:** Walls are procedurally instantiated based on cell adjacency rules. The `PlaceWalls` method iterates through the grid, checking neighbors for each cell type (Room, Hallway, Stairs) and instantiating wall prefabs where a walkable cell borders an empty space.

Once the geometry is generated, a Unity NavMesh is baked at runtime, providing the underlying navigation data required for the hybrid approach. The environment also features dynamic terrain elements, specifically "Slime Zones," which are sticky surfaces that significantly impede movement speed (applying a 0.5x speed multiplier). These zones are scattered stochastically across the map. The inclusion of these zones forces the agent to learn complex trade-offs between taking the shortest Euclidean path (which might be covered in slime) and a longer, faster path. This demonstrates the advantage of the RL approach over standard A\*, which typically optimizes only for distance. The random number generation is handled by a PCG-based RNG (`PcgRandom`), allowing for deterministic seeding to reproduce specific dungeon layouts for debugging or evaluation.

## Agent Architecture and Hybrid Control

The proposed "HybridAgent" operates using a hierarchical control scheme that blends classical pathfinding with deep reinforcement learning. Pure RL navigation in complex 3D environments often suffers from sparse rewards and poor long-term planning. To mitigate this, the agent utilizes the A*pathfinding data from the NavMesh to derive a "Steering Target"—an immediate sub-goal along the optimal path. This decomposes the problem: the A* planner handles global pathfinding, while the RL policy handles local control, physics interactions, and obstacle avoidance.

The agent's physical movement is driven by a Unity `CharacterController`, with custom physics logic handling gravity (including a fall gravity multiplier of 2.5x) and terminal velocity. This physics-based approach creates a non-trivial control problem involving inertia and momentum, unlike simple kinematic movement. Crucially, the agent explicitly synchronizes a silent `NavMeshAgent` to its position every frame (`SyncNavMeshAgent`). This ensures the A\* planner's state remains valid even as the RL policy deviates from the optimal path to avoid obstacles or slime, preventing the guidance system from providing stale data.

The RL policy is responsible for local locomotion and obstacle avoidance, receiving the steering target as a primary observation. The agent is trained using Proximal Policy Optimization (PPO), a state-of-the-art on-policy gradient method. The neural network consists of three fully connected layers with 256 hidden units each, utilizing a linear learning rate schedule starting at 3e-4. The training configuration employs a batch size of 2048 and a buffer size of 20,480, with a time horizon of 256 steps, ensuring stable updates over long-term trajectories.

## Observation and Action Spaces

The agent perceives its environment through a compact vector observation space designed to provide both proprioceptive and exteroceptive data.

- **Proprioception:** Agent position, `isGrounded` status, `isOnWall`, `isOnSticky` (slime).
- **Navigation:** Distance to final target, position of the immediate steering target, and direction vector towards that sub-goal. This "Steering Target" observation is the critical bridge between the classical planner and the neural network, providing immediate directional guidance.
- **Raycast Sensors:**
  - **Wall Detection:** 4 horizontal rays (Forward, Back, Left, Right) cast from the agent's center to detect proximity to obstacles.
  - **Terrain Analysis:** 4 downward-angled rays (originating 0.5 units up, angled towards `dir + down * 1.5`) to analyze the floor surface ahead for slime zones. These raycasts provide a local "lidar-like" view, essential for fine-grained obstacle avoidance that the high-level A\* planner cannot perceive.

The action space is hybrid in nature, consisting of continuous values for 2D planar movement (forward/backward velocity and lateral strafing) and a discrete binary action for jumping. The jumping mechanic is conditionally enabled only in later training phases to traverse obstacles. Modeling the jump as a discrete action simplifies the learning process for this high-commitment behavior compared to a continuous force input.

## Reward Structure

The reward function is carefully shaped to guide the agent towards efficient navigation while adhering to physical constraints.

- **Progress Rewards:**
  - **Steering Progress:** +0.1 \* delta distance towards the immediate steering target.
  - **Target Progress:** +0.05 \* delta distance towards the final goal.
  - **Movement Reward:** +0.01 per frame (if not stuck).
  - _Reasoning:_ These dense rewards solve the sparse reward problem inherent in large mazes, providing a continuous gradient of improvement.
- **Alignment:** +0.05 \* dot product of velocity and desired direction.
  - _Reasoning:_ Encourages smooth movement vectors that align with the intended path, reducing erratic behavior.
- **Terminal Reward:** +1.0 for reaching the target.
- **Penalties:**
  - **Time:** Constant penalty per step to encourage speed and efficiency.
  - **Wall Proximity:** Penalty scales linearly when distance < 0.6 units.
  - **Collisions:** -0.05 per frame for wall contact (plus -0.03 if pushing into it); -0.02 per frame for slime contact.
  - **Stagnation:** -0.05 per frame if the agent remains within 0.5 units of a position for more than 50 frames.
  - _Reasoning:_ These penalties shape the behavior away from local optima, such as wall-hugging or getting stuck in corners, and explicitly teach the agent to avoid high-cost terrain (slime).

## Curriculum Learning and Baseline Comparison

To manage the complexity of the task, a four-phase curriculum learning strategy is implemented.

1. **ReachTarget:** Single room (8x8 units). Goal: Simple target acquisition.
2. **BasePathfinding:** Two connected rooms. Goal: Basic path following without obstacles.
3. **FullPathfinding:** Full procedural dungeon generation. Goal: Complex navigation.
4. **AvoidSlime:** Full dungeon with "Slime Zones" enabled. Jumping action unlocked. Goal: Optimal pathing avoiding terrain penalties.

_Reasoning:_ Starting with a full dungeon is too difficult for an untrained agent. The curriculum builds basic motor control and path-following skills before introducing complex navigation and terrain optimization, significantly stabilizing training.

A critical component of this methodology is the use of a non-learning baseline agent ("BasicAgent") that utilizes standard Unity NavMesh navigation. This baseline serves as a dynamic pacer; the RL agent's episode is terminated with a failure state if it cannot reach the target within a short timeout after the baseline agent has succeeded. This mechanism forces the RL agent to be _competitive_ with standard techniques, creating a relative performance metric rather than just an absolute one. Progression between curriculum phases is automated, triggered only when the RL agent achieves a win rate exceeding 60% against the baseline agent over a rolling window of 50 episodes.
