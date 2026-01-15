import re
import csv
from pathlib import Path
from dataclasses import dataclass


@dataclass
class MapResult:
    map_id: int
    map_seed: int
    timestamp: str
    baseline_agent_time: float
    proposed_agent_time: float
    time_diff: float
    winner: str


def parse_log_file(log_path: Path) -> list[MapResult]:
    results: list[MapResult] = []
    
    start_pattern = re.compile(
        r'\[Eval\] Starting map (\d+) at (\d{2}:\d{2}:\d{2}\.\d{3}) on seed (\d+)'
    )
    complete_pattern = re.compile(
        r'\[Eval\] Map (\d+) complete \| Winner: (\w+) by ([\d.]+)s \| Hybrid: ([\d.]+)s \| Basic: ([\d.]+)s'
    )
    
    map_starts: dict[int, tuple[str, int, int]] = {}
    used_ids: set[int] = set()
    max_id: int = 0
    id_offset: int = 0
    
    with open(log_path, 'r', encoding='utf-8') as f:
        for line in f:
            line = line.strip()
            
            start_match = start_pattern.search(line)
            if start_match:
                original_map_id = int(start_match.group(1))
                timestamp = start_match.group(2)
                seed = int(start_match.group(3))
                
                if original_map_id <= max_id and original_map_id in used_ids:
                    id_offset = max_id
                
                actual_id = id_offset + original_map_id
                used_ids.add(actual_id)
                max_id = max(max_id, actual_id)
                
                map_starts[original_map_id] = (timestamp, seed, actual_id)
                continue
            
            complete_match = complete_pattern.search(line)
            if complete_match:
                original_map_id = int(complete_match.group(1))
                winner = complete_match.group(2)
                time_diff = float(complete_match.group(3))
                proposed_time = float(complete_match.group(4))
                baseline_time = float(complete_match.group(5))
                
                if original_map_id in map_starts:
                    timestamp, seed, actual_id = map_starts[original_map_id]
                    
                    results.append(MapResult(
                        map_id=actual_id,
                        map_seed=seed,
                        timestamp=timestamp,
                        baseline_agent_time=baseline_time,
                        proposed_agent_time=proposed_time,
                        time_diff=time_diff,
                        winner=winner,
                    ))
    
    return results


def write_results_to_csv(results: list[MapResult], output_path: Path) -> None:
    with open(output_path, 'w', newline='', encoding='utf-8') as f:
        writer = csv.writer(f)
        
        writer.writerow([
            'map_id',
            'map_seed', 
            'timestamp',
            'baseline_agent_time',
            'proposed_agent_time',
            'time_diff',
            'winner'
        ])
        
        for result in results:
            writer.writerow([
                result.map_id,
                result.map_seed,
                result.timestamp,
                result.baseline_agent_time,
                result.proposed_agent_time,
                result.time_diff,
                result.winner,
            ])


def clean_logs(logs_dir: Path, processed_dir: Path) -> None:
    processed_dir.mkdir(parents=True, exist_ok=True)
    
    for log_file in logs_dir.glob('*.txt'):
        print(f"Processing: {log_file.name}")
        results = parse_log_file(log_file)
        output_file = processed_dir / f"{log_file.stem}.csv"
        write_results_to_csv(results, output_file)
        print(f"  -> Wrote {len(results)} completed maps to {output_file.name}")


def run(base_dir: Path = None):
    if base_dir is None:
        base_dir = Path(__file__).parent
    
    logs_dir = base_dir / "logs"
    processed_dir = base_dir / "processed"
    
    print("Preprocessing evaluation logs...")
    print(f"  Logs directory: {logs_dir}")
    print(f"  Output directory: {processed_dir}")
    print()
    
    clean_logs(logs_dir, processed_dir)


if __name__ == "__main__":
    run()
