from pathlib import Path
import preprocess
import analysis


def main():
    base_dir = Path(__file__).parent
    
    print("=" * 60)
    print("ML Agent Evaluation Pipeline")
    print("=" * 60)
    print()
    
    preprocess.run(base_dir)
    print()
    
    analysis.main()


if __name__ == "__main__":
    main()
