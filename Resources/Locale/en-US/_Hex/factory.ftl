# Conveyor Junction Ports
signal-port-name-factory-conveyor-next-direction = Next Direction
signal-port-description-factory-conveyor-next-direction = Cycles the active output direction of this conveyor junction.

signal-port-name-factory-conveyor-direction-forward = Direction Forward
signal-port-description-factory-conveyor-direction-forward = Sets the output direction of this conveyor junction to forward.

signal-port-name-factory-conveyor-direction-right = Direction Right
signal-port-description-factory-conveyor-direction-right = Sets the output direction of this conveyor junction to right.

signal-port-name-factory-conveyor-direction-backward = Direction Backward
signal-port-description-factory-conveyor-direction-backward = Sets the output direction of this conveyor junction to backward.

signal-port-name-factory-conveyor-direction-left = Direction Left
signal-port-description-factory-conveyor-direction-left = Sets the output direction of this conveyor junction to left.

# Conveyor Junction Verbs and Popups
factory-conveyor-junction-verb-cycle = Cycle Direction
factory-conveyor-junction-verb-set = Set Direction: {$direction}
factory-conveyor-junction-switched = Conveyor routed: {$direction}

factory-conveyor-junction-dir-forward = Forward
factory-conveyor-junction-dir-right = Right
factory-conveyor-junction-dir-backward = Backward
factory-conveyor-junction-dir-left = Left

# Tool Stations
ent-FactoryToolStationWelder = factory welder station
    .desc = An automated stationary manipulator station that performs precision welding operations on items passing in front of it.

ent-FactoryToolStationScrewdriver = factory screwdriver station
    .desc = An automated stationary manipulator station that screws and unscrews components on items positioned in front of it.

ent-FactoryToolStationWirecutter = factory wirecutter station
    .desc = An automated stationary manipulator station that cuts and trims wiring on items positioned in front of it.

ent-FactoryToolStationCrowbar = factory crowbar station
    .desc = An automated stationary manipulator station that pries open panels and cases on items positioned in front of it.

ent-FactoryToolStationWrench = factory wrench station
    .desc = An automated stationary manipulator station that fastens and bolts mechanical structures on items positioned in front of it.

# Conveyor Junction Entities
ent-FactoryConveyorIntersection = conveyor intersection (4-way)
    .desc = An industrial conveyor crossroads capable of routing items in all 4 cardinal directions, one direction at a time.

ent-FactoryConveyorTJunction = conveyor T-junction
    .desc = An industrial conveyor T-junction capable of routing items along the main line or into a right branch.

ent-FactoryConveyorTJunctionFlipped = conveyor T-junction (flipped)
    .desc = An industrial conveyor T-junction capable of routing items along the main line or into a left branch.
