import pandas as pd
import numpy as np
from pathlib import Path
from scipy import stats
import matplotlib.pyplot as plt
import json
from dataclasses import dataclass, asdict
from typing import Literal


EPSILON = 0.0167


@dataclass
class ScenarioResult:
    scenario: str
    n_samples: int
    shapiro_stat: float
    shapiro_p: float
    is_normal: bool
    test_used: str
    test_stat: float
    test_p: float
    is_significant: bool
    mean_delta: float
    std_delta: float
    median_delta: float
    proposed_wins: int
    ties: int
    baseline_wins: int
    proposed_win_rate: float
    tie_rate: float
    baseline_win_rate: float


def load_scenario_data(processed_dir: Path) -> dict[str, pd.DataFrame]:
    scenarios = {}
    for csv_file in processed_dir.glob("*.csv"):
        scenario_name = csv_file.stem
        df = pd.read_csv(csv_file)
        df['delta'] = df['baseline_agent_time'] - df['proposed_agent_time']
        scenarios[scenario_name] = df
    return scenarios


def classify_winner(delta: float, epsilon: float = EPSILON) -> Literal['proposed', 'tie', 'baseline']:
    if delta > epsilon:
        return 'proposed'
    elif delta < -epsilon:
        return 'baseline'
    else:
        return 'tie'


def analyze_scenario(name: str, df: pd.DataFrame, alpha: float = 0.05) -> ScenarioResult:
    deltas = df['delta'].values
    n = len(deltas)
    
    if n >= 3:
        shapiro_stat, shapiro_p = stats.shapiro(deltas)
    else:
        shapiro_stat, shapiro_p = np.nan, np.nan
    is_normal = shapiro_p >= alpha if not np.isnan(shapiro_p) else False
    
    test_used = "Wilcoxon Signed-Rank"
    try:
        test_stat, test_p = stats.wilcoxon(deltas, alternative='greater')
    except ValueError:
        test_stat, test_p = np.nan, 1.0
    
    is_significant = test_p < alpha
    mean_delta = np.mean(deltas)
    std_delta = np.std(deltas, ddof=1)
    median_delta = np.median(deltas)
    
    df['classification'] = df['delta'].apply(classify_winner)
    proposed_wins = (df['classification'] == 'proposed').sum()
    ties = (df['classification'] == 'tie').sum()
    baseline_wins = (df['classification'] == 'baseline').sum()
    
    return ScenarioResult(
        scenario=name,
        n_samples=n,
        shapiro_stat=shapiro_stat,
        shapiro_p=shapiro_p,
        is_normal=is_normal,
        test_used=test_used,
        test_stat=test_stat,
        test_p=test_p,
        is_significant=is_significant,
        mean_delta=mean_delta,
        std_delta=std_delta,
        median_delta=median_delta,
        proposed_wins=proposed_wins,
        ties=ties,
        baseline_wins=baseline_wins,
        proposed_win_rate=proposed_wins / n * 100,
        tie_rate=ties / n * 100,
        baseline_win_rate=baseline_wins / n * 100,
    )


def create_box_plot(scenarios: dict[str, pd.DataFrame], output_path: Path):
    fig, ax = plt.subplots(figsize=(10, 6))
    
    data = []
    labels = []
    for name, df in sorted(scenarios.items()):
        data.append(df['delta'].values)
        labels.append(name.replace('_', '\n'))
    
    bp = ax.boxplot(data, labels=labels, patch_artist=True, 
                    showmeans=True, 
                    meanprops={"marker":"^", "markerfacecolor":"white", "markeredgecolor":"black"})
    
    colors = ['#60A5FA', '#67B8C4', '#FBBF24', '#A78BFA']
    
    for patch, median, color in zip(bp['boxes'], bp['medians'], colors[:len(bp['boxes'])]):
        patch.set_facecolor(color)
        patch.set_alpha(0.7)
        median.set_color('black')
        median.set_linewidth(1.5)
    
    ax.axhline(y=0, color='black', linestyle='--', linewidth=1, alpha=0.7)
    
    ax.set_xlabel('Scenario', fontsize=12)
    ax.set_ylabel('Time Difference (Baseline - Proposed) [s]', fontsize=12)
    ax.set_title('Performance Gap Distribution\n(Positive Values Indicate Proposed Agent is Faster)', fontsize=14)
    ax.grid(axis='y', alpha=0.3)
    
    plt.tight_layout()
    plt.savefig(output_path, dpi=300, bbox_inches='tight')
    plt.close()
    print(f"  -> Saved box plot to {output_path.name}")


def create_stacked_bar_chart(results: list[ScenarioResult], output_path: Path):
    fig, ax = plt.subplots(figsize=(10, 6))
    
    scenarios = [r.scenario.replace('_', '\n') for r in results]
    proposed_wins = [r.proposed_win_rate for r in results]
    ties = [r.tie_rate for r in results]
    baseline_wins = [r.baseline_win_rate for r in results]
    
    x = np.arange(len(scenarios))
    width = 0.6
    
    ax.bar(x, proposed_wins, width, label='Proposed', color='#60A5FA')
    ax.bar(x, ties, width, bottom=proposed_wins, label='Tie', color='#FBBF24')
    ax.bar(x, baseline_wins, width, bottom=np.array(proposed_wins) + np.array(ties), 
           label='Baseline', color='#F87171')
    
    ax.set_xlabel('Scenario', fontsize=12)
    ax.set_ylabel('Percentage (%)', fontsize=12)
    ax.set_title(f'Win Rate Distribution (ε = {EPSILON:.4f}s threshold)', fontsize=14)
    ax.set_xticks(x)
    ax.set_xticklabels(scenarios)
    ax.legend(loc='upper right')
    ax.set_ylim(0, 100)
    ax.grid(axis='y', alpha=0.3)
    
    for i, r in enumerate(results):
        if r.proposed_win_rate > 5:
            ax.text(i, r.proposed_win_rate / 2, f'{r.proposed_win_rate:.1f}%', 
                    ha='center', va='center', fontsize=10, fontweight='bold')
        if r.tie_rate > 0:
            ax.text(i, r.proposed_win_rate + r.tie_rate / 2, f'{r.tie_rate:.1f}%', 
                    ha='center', va='center', fontsize=9, fontweight='bold')
        if r.baseline_win_rate > 5:
            ax.text(i, r.proposed_win_rate + r.tie_rate + r.baseline_win_rate / 2, 
                    f'{r.baseline_win_rate:.1f}%', 
                    ha='center', va='center', fontsize=10, fontweight='bold')
    
    plt.tight_layout()
    plt.savefig(output_path, dpi=150, bbox_inches='tight')
    plt.close()
    print(f"  -> Saved stacked bar chart to {output_path.name}")


def save_results_json(results: list[ScenarioResult], output_path: Path):
    data = []
    for r in results:
        d = asdict(r)
        for key, value in d.items():
            if isinstance(value, (np.floating, np.float64)):
                d[key] = float(value)
            elif isinstance(value, (np.integer, np.int64)):
                d[key] = int(value)
            elif isinstance(value, np.bool_):
                d[key] = bool(value)
        data.append(d)
    with open(output_path, 'w', encoding='utf-8') as f:
        json.dump(data, f, indent=2)
    print(f"  -> Saved JSON results to {output_path.name}")


def main():
    script_dir = Path(__file__).parent
    processed_dir = script_dir / "processed"
    analysis_dir = script_dir / "analysis"
    
    analysis_dir.mkdir(parents=True, exist_ok=True)
    
    print("=" * 60)
    print("Statistical Analysis of ML Agent Evaluation")
    print("=" * 60)
    print()
    
    print("Loading scenario data...")
    scenarios = load_scenario_data(processed_dir)
    print(f"  Found {len(scenarios)} scenarios: {list(scenarios.keys())}")
    print()
    
    print("Analyzing scenarios...")
    results = []
    for name, df in sorted(scenarios.items()):
        print(f"  Analyzing: {name} (n={len(df)})")
        result = analyze_scenario(name, df)
        results.append(result)
        
        sig = "✓" if result.is_significant else "✗"
        print(f"    - Normality: p={result.shapiro_p:.4f} ({'Normal' if result.is_normal else 'Non-Normal'})")
        print(f"    - {result.test_used}: p={result.test_p:.4f} {sig}")
        print(f"    - Win Rate: Proposed {result.proposed_win_rate:.1f}% | Tie {result.tie_rate:.1f}% | Baseline {result.baseline_win_rate:.1f}%")
    print()
    
    print("Generating outputs...")
    create_box_plot(scenarios, analysis_dir / "performance_gap_boxplot.png")
    create_stacked_bar_chart(results, analysis_dir / "win_rate_stacked_bar.png")
    save_results_json(results, analysis_dir / "results.json")
    
    print()
    print("=" * 60)
    print("Analysis Complete!")
    print(f"Results saved to: {analysis_dir}")
    print("=" * 60)


if __name__ == "__main__":
    main()
