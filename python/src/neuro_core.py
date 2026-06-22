from .generated.grpc.actr.services import DecodeActionRequest, DecodeActionResponse, EvaluateConditionsRequest, EvaluateConditionsResponse, NeuroCoreServiceBase


class NeuroCore(NeuroCoreServiceBase):
    async def evaluate_conditions(self, evaluate_conditions_request: EvaluateConditionsRequest) -> EvaluateConditionsResponse:
        ids: list[str] = []
        for condition in evaluate_conditions_request.conditions:
            fields = condition.semantics.fields
            if "face" in fields and "resource crisis" in fields:
                ids.append(condition.rule_id)

        return EvaluateConditionsResponse(satisfied_rule_ids=ids)

    async def decode_action(self, decode_action_request: DecodeActionRequest) -> DecodeActionResponse:
        _ = decode_action_request
        raise NotImplementedError()
