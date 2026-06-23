import math

import asyncio
import copy
from dataclasses import dataclass
from ..generated.grpc.actr.services import Building, PerformActionRequest, PerformActionResponse
from ..generated.grpc.actr.services import BuildingsState, ResetRequest
from ..generated.grpc.actr.services import FrostpunkState
from ..generated.grpc.actr.services import FrostpunkWorldBase
from ..generated.grpc.actr.services import GetFullStateRequest
from ..generated.grpc.actr.services import LawsState
from ..generated.grpc.actr.services import ObserveBuildingsRequest
from ..generated.grpc.actr.services import ObserveLawsRequest
from ..generated.grpc.actr.services import ObservePopulationRequest
from ..generated.grpc.actr.services import ObserveResearchRequest
from ..generated.grpc.actr.services import ObserveResourcesRequest
from ..generated.grpc.actr.services import ObserveSocialRequest
from ..generated.grpc.actr.services import ObserveTemperatureRequest
from ..generated.grpc.actr.services import PopulationState
from ..generated.grpc.actr.services import ResearchState
from ..generated.grpc.actr.services import ResourceState
from ..generated.grpc.actr.services import SocialState
from ..generated.grpc.actr.services import TemperatureState


@dataclass
class BuildingInfo:
    cost: dict[str, int]
    max_workers: int
    heat_bonus: int = 0
    housing: int = 0
    research_per_worker: float = 0.0
    food_per_worker: int = 0
    coal_per_worker: int = 0
    steel_per_worker: int = 0
    heal_per_worker: int = 0


BUILDING_TYPES = {
    "tent": BuildingInfo(
        cost={"wood": 10},
        max_workers=0,
        heat_bonus=1,
        housing=2,
    ),
    "workshop": BuildingInfo(
        cost={"wood": 20, "steel": 5},
        max_workers=5,
        research_per_worker=0.5,
    ),
    "hunters_hut": BuildingInfo(
        cost={"wood": 15},
        max_workers=15,
        food_per_worker=2,
    ),
    "coal_mine": BuildingInfo(
        cost={"wood": 15, "steel": 5},
        max_workers=10,
        coal_per_worker=3,
    ),
    "steelworks": BuildingInfo(
        cost={"wood": 20, "steel": 10},
        max_workers=10,
        steel_per_worker=2,
    ),
    "infirmary": BuildingInfo(
        cost={"wood": 30, "steel": 10, "steam_cores": 1},
        max_workers=5,
        heal_per_worker=1,
    ),
}

ALL_LAWS = [
    "emergency_shift",
    "child_labor",
    "radical_treatment",
    "soup",
]

ALL_TECHS = {
    "heaters": {"cost_hours": 20, "unlocks": "heat_level +1"},
    "steam_hubs": {"cost_hours": 30, "unlocks": "range_heat"},
    "advanced_steelworks": {"cost_hours": 40, "unlocks": "steel production x2"},
    "better_hunters": {"cost_hours": 25, "unlocks": "food_per_worker +2"},
}

MAP_SIZE = 5


# noinspection PyAttributeOutsideInit
class FrostpunkGame(FrostpunkWorldBase):
    def __init__(self, scenario: str = "default"):
        self.scenario = scenario
        asyncio.run(self.reset(ResetRequest()))

    async def reset(self, reset_request: ResetRequest):
        _ = reset_request

        self.day = 1
        self.time_of_day = 0.0
        self.resources = {"coal": 100, "wood": 50, "steel": 20, "food": 100, "steam_cores": 2}
        self.heat_level = 1
        self.outside_temp = -20.0
        self.temperature = -20.0

        self.workers = 15
        self.engineers = 5
        self.children = 4
        self.sick = 0
        self.hungry = 0.0

        self.hope = 50.0
        self.discontent = 0.0

        self.enacted_laws: list[str] = []
        self.researched_techs: list[str] = []
        self.current_research = ""
        self.research_progress = 0.0
        self.law_cooldown = 0

        self.buildings: dict[str, Building] = {}
        self._next_building_id = 0

        self._add_building("tent", 0, 0, level=2)
        self._add_building("tent", 2, 2)
        self._add_building("workshop", 2, 1)
        self._add_building("hunters_hut", 3, 2)

        self._auto_assign_workers()

        return self._get_state()

    def _add_building(self, btype: str, x: int, y: int, level: int = 1) -> str:
        bid = f"b{self._next_building_id}"
        self._next_building_id += 1
        self.buildings[bid] = Building(
            id=bid, type=btype, level=level, x=x, y=y, assigned_workers=0, is_active=True
        )
        return bid

    def _get_state(self) -> FrostpunkState:
        return FrostpunkState(
            day=self.day,
            time_of_day=0.0,
            resources=copy.deepcopy(self.resources),
            population=PopulationState(
                total_population=self.workers + self.engineers + self.children,
                workers=self.workers,
                engineers=self.engineers,
                children=self.children,
                sick=self.sick,
                hungry=self.hungry,
            ),
            social=SocialState(hope=self.hope, discontent=self.discontent),
            temperature=TemperatureState(temperature=self.temperature, heat_level=self.heat_level),
            laws=LawsState(enacted_laws=copy.deepcopy(self.enacted_laws)),
            research=ResearchState(
                researched_techs=copy.deepcopy(self.researched_techs),
                current_research_id=self.current_research,
                research_progress=self.research_progress,
            ),
            buildings=BuildingsState(buildings=list(self.buildings.values())),
        )

    def _auto_assign_workers(self):
        for b in self.buildings.values():
            b.assigned_workers = 0

        available_workers = self.workers
        available_engineers = self.engineers

        for b in self.buildings.values():
            if b.type == "workshop" and available_engineers > 0:
                assign = min(available_engineers, BUILDING_TYPES[b.type].max_workers)
                b.assigned_workers = assign
                available_engineers -= assign

        for b in self.buildings.values():
            if b.type in ("hunters_hut", "coal_mine") and available_workers > 0:
                assign = min(available_workers, BUILDING_TYPES[b.type].max_workers)
                b.assigned_workers = assign
                available_workers -= assign

    async def perform_action(self, perform_action_request: PerformActionRequest) -> PerformActionResponse:
        action = perform_action_request.action
        reward = 0.0

        if action.build:
            build = action.build
            btype = build.building_type
            x, y = build.x, build.y
            cost = BUILDING_TYPES[btype].cost
            if self._can_build(x, y, cost):
                self._consume_resources(cost)
                self._add_building(btype, x, y)
                reward += 2
        elif action.demolish:
            bid = action.demolish.building_id
            if bid in self.buildings and self.buildings[bid].type != "tent" or self.buildings[bid].level > 1:
                del self.buildings[bid]
                reward += 1
        elif action.assign_workers:
            bid = action.assign_workers.building_id
            count = action.assign_workers.count
            if bid in self.buildings:
                b = self.buildings[bid]
                max_w = BUILDING_TYPES[b.type].max_workers
                b.assigned_workers = min(count, max_w)
        elif action.enact_law:
            law = action.enact_law.law_id
            if law in ALL_LAWS and law not in self.enacted_laws and self.law_cooldown <= 0:
                self.enacted_laws.append(law)
                self.law_cooldown = 2
                if law == "emergency_shift":
                    self.discontent += 10
                elif law == "child_labor":
                    self.hope -= 10
                elif law == "radical_treatment":
                    self.hope -= 5
                elif law == "soup":
                    self.discontent += 5
                reward += 5
        elif action.research:
            tech = action.research.tech_id
            if tech in ALL_TECHS and tech not in self.researched_techs:
                self.current_research = tech
                self.research_progress = 0.0
        elif action.adjust_heater:
            new_level = action.adjust_heater.new_level
            self.heat_level = max(0, min(3, new_level))

        self._simulate_day()
        reward += self._compute_reward()
        done = self._check_done()
        if done and self.hope <= 0:
            reward -= 10

        return PerformActionResponse(new_state=self._get_state(), done=done, reward=reward)

    def _can_build(self, x, y, cost) -> bool:
        for res, amount in cost.items():
            if self.resources.get(res, 0) < amount:
                return False
        for b in self.buildings.values():
            if b.x == x and b.y == y:
                return False
        if not (0 <= x < MAP_SIZE and 0 <= y < MAP_SIZE):
            return False
        return True

    def _consume_resources(self, cost):
        for res, amount in cost.items():
            self.resources[res] -= amount

    def _simulate_day(self):
        self.outside_temp -= 1.5
        self.temperature = self.outside_temp + self.heat_level * 10

        coal_consumption = self.heat_level * 5
        food_per_capita = 0.5
        if "soup" in self.enacted_laws:
            food_per_capita = 0.25
        total_pop = self.workers + self.engineers + self.children
        food_consumption = total_pop * food_per_capita

        coal_produced = 0
        food_produced = 0
        steel_produced = 0
        research_hours = 0
        healing = 0

        for b in self.buildings.values():
            if not b.is_active:
                continue
            w = b.assigned_workers
            if b.type == "coal_mine":
                coal_produced += w * BUILDING_TYPES["coal_mine"].coal_per_worker
                if "emergency_shift" in self.enacted_laws:
                    coal_produced *= 1.5
            elif b.type == "hunters_hut":
                base = BUILDING_TYPES["hunters_hut"].food_per_worker
                if "better_hunters" in self.researched_techs:
                    base += 2
                food_produced += w * base
            elif b.type == "steelworks":
                steel_produced += w * BUILDING_TYPES["steelworks"].steel_per_worker
                if "emergency_shift" in self.enacted_laws:
                    steel_produced *= 1.5
            elif b.type == "workshop":
                research_hours += w * BUILDING_TYPES["workshop"].research_per_worker
            elif b.type == "infirmary":
                healing += w * BUILDING_TYPES["infirmary"].heal_per_worker
                if "radical_treatment" in self.enacted_laws:
                    healing *= 2

        self.resources["coal"] += math.floor(coal_produced - coal_consumption)
        self.resources["food"] += math.floor(food_produced - food_consumption)
        self.resources["steel"] += math.floor(steel_produced)

        if self.resources["food"] < 0:
            self.hungry = min(total_pop, -self.resources["food"] // 0.5)
            self.resources["food"] = 0
        else:
            self.hungry = 0

        if self.temperature < -20:
            new_sick = max(0, int(total_pop * 0.1))
            self.sick = min(total_pop, self.sick + new_sick)
        healing_capacity = healing
        self.sick = max(0, self.sick - healing_capacity)

        if self.hungry > 0:
            self.hope -= 2 * self.hungry
        if self.sick > 0:
            self.hope -= 1 * self.sick
        if "emergency_shift" in self.enacted_laws:
            self.discontent += 2
        if "child_labor" in self.enacted_laws:
            self.hope -= 1

        self.hope = max(0, self.hope)
        self.discontent = max(0, self.discontent)

        if self.current_research:
            needed = ALL_TECHS[self.current_research]["cost_hours"]
            self.research_progress += research_hours / 24
            if self.research_progress >= needed:
                self.researched_techs.append(self.current_research)
                if self.current_research == "heaters":
                    self.heat_level = min(3, self.heat_level + 1)
                self.current_research = ""
                self.research_progress = 0.0

        if self.law_cooldown > 0:
            self.law_cooldown -= 1

        self.day += 1

    def _compute_reward(self) -> float:
        reward = 1.0
        reward += 0.1 * (self.hope - 50)
        reward -= 0.1 * self.discontent
        if self.current_research == "" and self.research_progress == 0:
            pass
        return reward

    def _check_done(self) -> bool:
        if self.hope <= 0:
            return True
        if self.workers + self.engineers + self.children == 0:
            return True
        if self.day > 40:
            return True
        return False

    async def observe_resources(self, observe_resources_request: ObserveResourcesRequest) -> ResourceState:
        _ = observe_resources_request
        return ResourceState(
            day=self.day,
            time_of_day=self.time_of_day,
            resources={k: v for k, v in self.resources.items()}
        )

    async def observe_population(self, observe_population_request: ObservePopulationRequest) -> PopulationState:
        _ = observe_population_request
        return PopulationState(
            total_population=self.workers + self.engineers + self.children,
            workers=self.workers,
            engineers=self.engineers,
            children=self.children,
            sick=self.sick,
            hungry=self.hungry
        )

    async def observe_social(self, observe_social_request: ObserveSocialRequest) -> SocialState:
        _ = observe_social_request
        return SocialState(hope=self.hope, discontent=self.discontent)

    async def observe_temperature(self, observe_temperature_request: ObserveTemperatureRequest) -> TemperatureState:
        _ = observe_temperature_request
        return TemperatureState(temperature=self.temperature, heat_level=self.heat_level)

    async def observe_laws(self, observe_laws_request: ObserveLawsRequest) -> LawsState:
        _ = observe_laws_request
        return LawsState(enacted_laws=list(self.enacted_laws))

    async def observe_research(self, observe_research_request: ObserveResearchRequest) -> ResearchState:
        _ = observe_research_request
        return ResearchState(
            researched_techs=list(self.researched_techs),
            current_research_id=self.current_research,
            research_progress=self.research_progress
        )

    async def observe_buildings(self, observe_buildings_request: ObserveBuildingsRequest) -> BuildingsState:
        _ = observe_buildings_request
        return BuildingsState(buildings=list(self.buildings.values()))

    async def get_full_state(self, get_full_state_request: GetFullStateRequest) -> FrostpunkState:
        _ = get_full_state_request
        return self._get_state()
