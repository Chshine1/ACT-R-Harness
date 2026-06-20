from typing import Optional

from .interfaces import AbstractNeuroCore
from .models import Op, Rule


class MockNeuroCore(AbstractNeuroCore):
    def translate_action(self, action_desc: str, context: dict) -> list[Op]:
        if "most urgent" in action_desc:
            return [
                Op(target="visual", command="query", params={"what": "all_resources"}),
                Op(target="goal", command="set", params={"focus": "find_most_critical"})
            ]
        return [Op(target="manual", command="noop")]

    def condition_holds(self, condition_desc: str, context: dict) -> bool:
        if "face" in condition_desc and "resource crisis" in condition_desc:
            return True
        return False

    def propose_rule(self, trajectory: list) -> Optional[Rule]:
        return None
