from typing import Any

from ..buffers_view import BuffersView


class SymbolicMatcher:
    def evaluate(self, condition_node: dict[str, Any], view: BuffersView) -> bool:
        t = condition_node.get("type")
        if t == "and":
            return all(self.evaluate(c, view) for c in condition_node["conditions"])
        if t == "or":
            return any(self.evaluate(c, view) for c in condition_node["conditions"])
        if t == "not":
            return not self.evaluate(condition_node["condition"], view)
        if t == "equals":
            return view.get(condition_node["slot"]) == condition_node["value"]
        if t == "exist":
            return view.get(condition_node["slot"]) is not None
        return False
