class MiniFrostPunk:
    def __init__(self):
        self.food = 100
        self.coal = 100
        self.day = 0

    def reset(self):
        self.food = 100
        self.coal = 100
        self.day = 0
        return {"food": self.food, "coal": self.coal, "day": self.day}

    def step(self, action):
        if action == "produce_food":
            self.food += 20
            self.coal -= 10
        elif action == "produce_coal":
            self.coal += 30
            self.food -= 5
        self.day += 1
        reward = min(self.food, self.coal) / 100.0
        done = self.day >= 50 or self.food <= 0 or self.coal <= 0
        return {"food": self.food, "coal": self.coal}, reward, done
