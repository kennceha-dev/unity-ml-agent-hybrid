# Unity ML-Agents

## Setup and Installation

### Installation with uv

1. Install uv:

   ```bash
    # On macOS and Linux.
    curl -LsSf https://astral.sh/uv/install.sh | sh
    # On Windows.
    powershell -ExecutionPolicy ByPass -c "irm https://astral.sh/uv/install.ps1 | iex"
   ```

2. Install Python 3.10.12:

   ```bash
   uv python install 3.10.12
   ```

3. Install all dependencies using uv sync:

   ```bash
   uv sync
   ```

4. Activate the virtual environment:

   ```bash
   # On Windows:
   .venv\Scripts\activate
   # On Mac/Linux:
   source .venv/bin/activate

   # `deactivate` to exit the virtual environment
   ```

5. Verify ML-Agents installation:

   ```bash
   mlagents-learn --help
   ```

## Training a New Model

Start training with the following command:

```bash
mlagents-learn <path-to-config> --run-id=<run-name> --time-scale=<time-scale>
```

For example:

```bash
mlagents-learn config.yaml --run-id=Run --time-scale=1
```

### Parameters

- `--run-id`: Unique identifier for your training session (required)
- `config.yaml`: Path to your configuration file (optional)
- `--time-scale`: Speed of the environment (default is 20). Use `1` for real-time simulation.

## Resuming Training

To resume training from a previous session:

```bash
mlagents-learn <path-to-config> --run-id=<run-name> --time-scale=<time-scale> --resume
```

For example:

```bash
mlagents-learn config.yaml --run-id=Run --time-scale=1 --resume
```

The `--resume` flag tells ML-Agents to continue training from the last saved checkpoint of the specified run-id.

## Using TensorBoard

TensorBoard allows you to monitor training metrics in real-time.

1. Start TensorBoard:

   ```bash
   tensorboard --logdir=results
   ```

2. Open your browser and navigate to `http://localhost:6006`

3. Key metrics to monitor:
   - Cumulative Reward: Overall performance
   - Episode Length: How long episodes last
   - Policy Loss: Indicates stability of learning
   - Value Loss: How well the value function is being learned

You can compare multiple training runs side by side in TensorBoard.

## Common Commands Cheatsheet

```bash
# Basic training with default parameters
mlagents-learn

# Training with custom configuration
mlagents-learn path/to/config.yaml --run-id=unique_name

# Resume training from previous checkpoint
mlagents-learn path/to/config.yaml --run-id=unique_name --resume

# Training with Unity executable
mlagents-learn path/to/config.yaml --env=path/to/UnityApp.exe

# Force overwriting previous training with same run-id
mlagents-learn path/to/config.yaml --run-id=unique_name --force

# Run without graphics (headless) for faster training
mlagents-learn path/to/config.yaml --run-id=unique_name --no-graphics
```
