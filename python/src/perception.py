from .interfaces import AbstractPerceptionMotor
from .world.frostpunk_sim import MiniFrostPunk


class FrostPunkPerception(AbstractPerceptionMotor):
    def __init__(self, world: MiniFrostPunk):
        self.world = world
        self._last_state: dict[str, int] = {}

    def query_visual(self, description: str) -> dict:
        return {
            "food": self.world.food,
            "coal": self.world.coal,
            "day": self.world.day,
        }

    def execute_manual(self, action: str, params: dict) -> dict:
        state, reward, done = self.world.step(action)
        self._last_state = state
        return {"state": state, "reward": reward, "done": done}
