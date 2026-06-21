import subprocess
import sys
from pathlib import Path

PROTO_DIR = Path("../shared/proto")
OUT_DIR = Path("./src/generated/grpc")


def ensure_dir(path: Path) -> None:
    path.mkdir(parents=True, exist_ok=True)


def clean_generated(path: Path) -> None:
    if path.exists():
        for f in path.glob("*"):
            if f.is_file():
                f.unlink()
        for d in sorted(path.glob("**/*"), reverse=True):
            if d.is_dir() and not any(d.iterdir()):
                d.rmdir()


def find_proto_files(proto_dir: Path) -> list[Path]:
    return list(proto_dir.rglob("*.proto"))


def generate() -> None:
    ensure_dir(OUT_DIR)
    clean_generated(OUT_DIR)

    proto_files = find_proto_files(PROTO_DIR)
    if not proto_files:
        print("No .proto files found.")
        return

    cmd = [
              sys.executable, "-m", "grpc_tools.protoc",
              f"--proto_path={PROTO_DIR}",
              f"--python_betterproto_out={OUT_DIR}",
          ] + [str(p) for p in proto_files]

    print("Running:", " ".join(cmd))
    result = subprocess.run(cmd, capture_output=True, text=True)

    if result.returncode != 0:
        print(result.stderr)
        if result.stdout:
            print("stdout:", result.stdout)
        raise RuntimeError("protoc failed")


if __name__ == "__main__":
    generate()
