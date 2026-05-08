# Manual Audit — Axes - D3D v18.10.pdf

Package: `d3d_v18_10` | Pages: 97 | Rollups: 16

## Coverage Summary

| Template | Axis | Manual steps | Authored in scope | Coverage |
|---|---|---:|---:|---:|
| RodAssembly | z_front | 6 | 2 | 🔴 33% |
| RodAssembly | y_right | 6 | 2 | 🔴 33% |
| RodAssembly | z_back | 4 | 2 | 🔴 25% |
| RodAssembly | y_left | 4 | 2 | 🔴 25% |
| RodAssembly | x_axis | 3 | 2 | 🔴 0% |
| MotorHolder | z_front | 6 | 10 | 🟡 50% |
| MotorHolder | y_right | 6 | 10 | 🟡 50% |
| MotorHolder | z_back | 4 | 10 | 🟡 50% |
| MotorHolder | y_left | 4 | 12 | 🟡 50% |
| MotorHolder | x_axis | 4 | 12 | 🟢 100% |
| IdlerHalves | z_front | 6 | 6 | 🔴 33% |
| IdlerHalves | y_right | 6 | 6 | 🔴 33% |
| IdlerHalves | z_back | 7 | 6 | 🔴 43% |
| IdlerHalves | y_left | 7 | 6 | 🔴 29% |
| IdlerHalves | x_axis | 6 | 6 | 🔴 17% |
| BeltThread | z_front | 2 | 9 | 🟡 50% |
| BeltThread | y_right | 2 | 9 | 🟡 50% |
| BeltThread | z_back | 6 | 9 | 🟡 83% |
| BeltThread | y_left | 6 | 9 | 🟡 83% |
| BeltThread | x_axis | 2 | 9 | 🟡 50% |
| BearingCarriage | z_front | 0 | 8 | 🔴 0% |
| BearingCarriage | y_right | 0 | 8 | 🔴 0% |
| BearingCarriage | z_back | 0 | 8 | 🔴 0% |
| BearingCarriage | y_left | 0 | 8 | 🔴 0% |
| BearingCarriage | x_axis | 0 | 12 | 🔴 0% |


---

## BearingCarriage — axes: x_axis (manual pp. 66–67)

**Manual sub-steps (0):**

### x_axis ← `assembly_d3d_x_axis_bench.json`
Authored in scope: 12 | Coverage of manual: 0/0 (0%)

**ℹ Authored steps with no manual match (12):**
- seq 181 `Place 4 bearings in carriage half` (Place, parts=['d3d_x_axis_half_carriage'])
- seq 182 `Shake test: bearings must not rattle` (Confirm)
- seq 183 `Rod slide test: slight resistance, not too free` (Confirm)
- seq 184 `Bolt carriage halves together` (Use)
- seq 186 `Stack two flanged bearings onto bolt` (Place)
- seq 188 `Bolt idler halves and secure bearing` (Use)
- seq 200 `Slide carriage onto rods` (Place, parts=['d3d_x_axis_rod_pair', 'd3d_x_axis_carriage_side'])
- seq 202 `QC: rods flush, carriage slides freely` (Confirm)
- seq 203 `Thread belt through large carriage hole` (Place)
- seq 204 `Route belt around idler bearing` (Place)
- seq 205 `Thread belt back through small carriage hole` (Place)
- seq 209 `Travel test: carriage moves smoothly with belt` (Confirm)


---

## BearingCarriage — axes: y_left, z_back (manual pp. 7–8)

**Manual sub-steps (0):**

### y_left ← `assembly_d3d_y_left_bench.json`
Authored in scope: 8 | Coverage of manual: 0/0 (0%)

**ℹ Authored steps with no manual match (8):**
- seq 119 `Stack two flanged bearings onto bolt` (Place, parts=['y_left_625zz_a', 'y_left_625zz_b'])
- seq 135 `Slide carriage onto rods` (Place, parts=['y1_bracket', 'pocket039', 'pocket040'])
- seq 137 `QC: rods flush, carriage slides freely` (Confirm)
- seq 138 `Thread belt through large carriage hole` (Place)
- seq 139 `Route belt around idler bearing` (Place)
- seq 140 `Thread belt back through small carriage hole` (Place)
- seq 144 `Travel test: carriage moves smoothly with belt` (Confirm)
- seq 121 `Tighten M6x18 inner bolt against bearings` (Use, parts=['y_left_idler_m6_nut_inner'])

### z_back ← `assembly_d3d_z_back_bench.json`
Authored in scope: 8 | Coverage of manual: 0/0 (0%)

**ℹ Authored steps with no manual match (8):**
- seq 244 `Stack two flanged bearings onto bolt` (Place)
- seq 246 `Bolt idler halves and secure bearing` (Use)
- seq 258 `Slide carriage onto rods` (Place, parts=['motor_piece001'])
- seq 260 `QC: rods flush, carriage slides freely` (Confirm)
- seq 261 `Thread belt through large carriage hole` (Place)
- seq 262 `Route belt around idler bearing` (Place)
- seq 263 `Thread belt back through small carriage hole` (Place)
- seq 267 `Travel test: carriage moves smoothly with belt` (Confirm)


---

## BearingCarriage — axes: y_right, z_front (manual pp. 43–44)

**Manual sub-steps (0):**

### y_right ← `assembly_d3d_y_right_bench.json`
Authored in scope: 8 | Coverage of manual: 0/0 (0%)

**ℹ Authored steps with no manual match (8):**
- seq 153 `Stack two flanged bearings onto bolt` (Place)
- seq 155 `Bolt idler halves and secure bearing` (Use)
- seq 167 `Slide carriage onto rods` (Place, parts=['y2_bracket'])
- seq 169 `QC: rods flush, carriage slides freely` (Confirm)
- seq 170 `Thread belt through large carriage hole` (Place)
- seq 171 `Route belt around idler bearing` (Place)
- seq 172 `Thread belt back through small carriage hole` (Place)
- seq 176 `Travel test: carriage moves smoothly with belt` (Confirm)

### z_front ← `assembly_d3d_z_front_bench.json`
Authored in scope: 8 | Coverage of manual: 0/0 (0%)

**ℹ Authored steps with no manual match (8):**
- seq 218 `Stack two flanged bearings onto bolt` (Place)
- seq 220 `Bolt idler halves and secure bearing` (Use)
- seq 232 `Slide carriage onto rods` (Place, parts=['motor_piece'])
- seq 234 `QC: rods flush, carriage slides freely` (Confirm)
- seq 235 `Thread belt through large carriage hole` (Place)
- seq 236 `Route belt around idler bearing` (Place)
- seq 237 `Thread belt back through small carriage hole` (Place)
- seq 241 `Travel test: carriage moves smoothly with belt` (Confirm)


---

## BeltThread — axes: x_axis (manual pp. 69–83)

**Manual sub-steps (2):**
- p.69 Step 14.1: Assemble Carriage
- p.82 Step 18.5: Thread belt into carriage

### x_axis ← `assembly_d3d_x_axis_bench.json`
Authored in scope: 9 | Coverage of manual: 1/2 (50%)

**Matched (manual → authored):**
- Step 18.5 `Thread belt into carriage` → seq 203 `Thread belt through large carriage hole` (50%)

**⚠ Manual steps NOT covered (1):**
- p.69 Step 14.1: Assemble Carriage

**ℹ Authored steps with no manual match (8):**
- seq 192 `Insert belt in motor holder channel` (Place)
- seq 194 `Confirm belt runs smoothly on pulley` (Confirm)
- seq 204 `Route belt around idler bearing` (Place)
- seq 205 `Thread belt back through small carriage hole` (Place)
- seq 206 `Confirm belt peg orientation before locking` (Confirm, parts=['fastener_x_axis_belt_peg'])
- seq 207 `Lock first belt peg` (Use)
- seq 208 `Insert second peg loosely` (Place)
- seq 209 `Travel test: carriage moves smoothly with belt` (Confirm)


---

## BeltThread — axes: y_left, z_back (manual pp. 10–32)

**Manual sub-steps (6):**
- p.10 Step 2.1: Assemble Carriage
- p.27 Step 7.1: Thread belt into carriage
- p.29 Step 7.2: Thread belt around idler bearing (continued)
- p.30 Step 7.3: Thread belt through one peg
- p.31 Step 7.4: Tighten ﬁrst peg into carriage
- p.32 Step 7.5: Tighten second peg into carriage

### y_left ← `assembly_d3d_y_left_bench.json`
Authored in scope: 9 | Coverage of manual: 5/6 (83%)

**Matched (manual → authored):**
- Step 7.1 `Thread belt into carriage` → seq 138 `Thread belt through large carriage hole` (50%)
- Step 7.2 `Thread belt around idler bearing (continued)` → seq 139 `Route belt around idler bearing` (67%)
- Step 7.3 `Thread belt through one peg` → seq 140 `Thread belt back through small carriage hole` (43%)
- Step 7.4 `Tighten ﬁrst peg into carriage` → seq 142 `Lock first belt peg` (25%)
- Step 7.5 `Tighten second peg into carriage` → seq 143 `Insert second peg loosely` (50%)

**⚠ Manual steps NOT covered (1):**
- p.10 Step 2.1: Assemble Carriage

**ℹ Authored steps with no manual match (4):**
- seq 127 `Insert belt in motor holder channel` (Place, parts=['y_left_gt2_belt'])
- seq 129 `Confirm belt runs smoothly on pulley` (Confirm)
- seq 141 `Confirm belt peg orientation before locking` (Confirm)
- seq 144 `Travel test: carriage moves smoothly with belt` (Confirm)

### z_back ← `assembly_d3d_z_back_bench.json`
Authored in scope: 9 | Coverage of manual: 5/6 (83%)

**Matched (manual → authored):**
- Step 7.1 `Thread belt into carriage` → seq 261 `Thread belt through large carriage hole` (50%)
- Step 7.2 `Thread belt around idler bearing (continued)` → seq 262 `Route belt around idler bearing` (67%)
- Step 7.3 `Thread belt through one peg` → seq 263 `Thread belt back through small carriage hole` (43%)
- Step 7.4 `Tighten ﬁrst peg into carriage` → seq 265 `Lock first belt peg` (25%)
- Step 7.5 `Tighten second peg into carriage` → seq 266 `Insert second peg loosely` (50%)

**⚠ Manual steps NOT covered (1):**
- p.10 Step 2.1: Assemble Carriage

**ℹ Authored steps with no manual match (4):**
- seq 250 `Insert belt in motor holder channel` (Place)
- seq 252 `Confirm belt runs smoothly on pulley` (Confirm)
- seq 264 `Confirm belt peg orientation before locking` (Confirm, parts=['fastener_z_back_belt_peg'])
- seq 267 `Travel test: carriage moves smoothly with belt` (Confirm)


---

## BeltThread — axes: y_right, z_front (manual pp. 46–59)

**Manual sub-steps (2):**
- p.46 Step 2.1: Assemble Carriage
- p.59 Step 12.1: Tighten Belt into carriage

### y_right ← `assembly_d3d_y_right_bench.json`
Authored in scope: 9 | Coverage of manual: 1/2 (50%)

**Matched (manual → authored):**
- Step 12.1 `Tighten Belt into carriage` → seq 170 `Thread belt through large carriage hole` (33%)

**⚠ Manual steps NOT covered (1):**
- p.46 Step 2.1: Assemble Carriage

**ℹ Authored steps with no manual match (8):**
- seq 159 `Insert belt in motor holder channel` (Place)
- seq 161 `Confirm belt runs smoothly on pulley` (Confirm)
- seq 171 `Route belt around idler bearing` (Place)
- seq 172 `Thread belt back through small carriage hole` (Place)
- seq 173 `Confirm belt peg orientation before locking` (Confirm)
- seq 174 `Lock first belt peg` (Use)
- seq 175 `Insert second peg loosely` (Place)
- seq 176 `Travel test: carriage moves smoothly with belt` (Confirm)

### z_front ← `assembly_d3d_z_front_bench.json`
Authored in scope: 9 | Coverage of manual: 1/2 (50%)

**Matched (manual → authored):**
- Step 12.1 `Tighten Belt into carriage` → seq 235 `Thread belt through large carriage hole` (33%)

**⚠ Manual steps NOT covered (1):**
- p.46 Step 2.1: Assemble Carriage

**ℹ Authored steps with no manual match (8):**
- seq 224 `Insert belt in motor holder channel` (Place)
- seq 226 `Confirm belt runs smoothly on pulley` (Confirm)
- seq 236 `Route belt around idler bearing` (Place)
- seq 237 `Thread belt back through small carriage hole` (Place)
- seq 238 `Confirm belt peg orientation before locking` (Confirm, parts=['fastener_z_front_belt_peg'])
- seq 239 `Lock first belt peg` (Use)
- seq 240 `Insert second peg loosely` (Place)
- seq 241 `Travel test: carriage moves smoothly with belt` (Confirm)


---

## IdlerHalves — axes: (none) (manual pp. 95)

**Manual sub-steps (1):**
- p.95 Step 22: Insert rods in the idler piece.

_No axis instances detected — informational page._


---

## IdlerHalves — axes: x_axis (manual pp. 71–72)

**Manual sub-steps (6):**
- p.71 Step 15.1: Assemble the idler
- p.71 Step 15.2: Insert M6x18 bolt
- p.71 Step 15.3: Put bearings together
- p.72 Step 15.2: Place nuts in idler
- p.72 Step 15.3: Add [2] M6x18 bolts
- p.72 Step 15.4: Close up the idler

### x_axis ← `assembly_d3d_x_axis_bench.json`
Authored in scope: 6 | Coverage of manual: 1/6 (17%)

**Matched (manual → authored):**
- Step 15.2 `Insert M6x18 bolt` → seq 185 `Insert M6x18 bolt into idler half` (60%)

**⚠ Manual steps NOT covered (5):**
- p.71 Step 15.1: Assemble the idler
- p.71 Step 15.3: Put bearings together
- p.72 Step 15.2: Place nuts in idler
- p.72 Step 15.3: Add [2] M6x18 bolts
- p.72 Step 15.4: Close up the idler

**ℹ Authored steps with no manual match (5):**
- seq 187 `Align idler rod holes before closing` (Confirm)
- seq 188 `Bolt idler halves and secure bearing` (Use)
- seq 198 `Insert rods into idler, ends flush` (Place, parts=['rod_009', 'rod_010'])
- seq 199 `Tighten short idler bolts onto rods` (Use)
- seq 204 `Route belt around idler bearing` (Place)


---

## IdlerHalves — axes: y_left, z_back (manual pp. 12–24)

**Manual sub-steps (7):**
- p.12 Step 3.1: Insert M6x18 Bolt
- p.12 Step 3.2: Insert two bearing pieces
- p.12 Step 3.3: Identify how to put the idler
- p.13 Step 3.4: Secure bearing with one M6x18 bolt
- p.13 Step 3.5: Put 1 m6x30 screw into correct hole
- p.14 Step 3.6: Secure a M6x18 bolt
- p.24 Step 6.2: Tighten the idler bolts

### y_left ← `assembly_d3d_y_left_bench.json`
Authored in scope: 6 | Coverage of manual: 2/7 (29%)

**Matched (manual → authored):**
- Step 3.1 `Insert M6x18 Bolt` → seq 118 `Insert M6x18 bolt into idler half` (60%)
- Step 6.2 `Tighten the idler bolts` → seq 134 `Tighten short idler bolts onto rods` (60%)

**⚠ Manual steps NOT covered (5):**
- p.12 Step 3.2: Insert two bearing pieces
- p.12 Step 3.3: Identify how to put the idler
- p.13 Step 3.4: Secure bearing with one M6x18 bolt
- p.13 Step 3.5: Put 1 m6x30 screw into correct hole
- p.14 Step 3.6: Secure a M6x18 bolt

**ℹ Authored steps with no manual match (4):**
- seq 120 `Place second idler half and align rod holes` (Place, parts=['idler002_half_b'])
- seq 133 `Insert rods into idler, ends flush` (Place, parts=['rod_005', 'rod_006'])
- seq 139 `Route belt around idler bearing` (Place)
- seq 123 `Insert M6x18 in last idler hole loosely` (Place, parts=['y_left_idler_m6x18_loose', 'y_left_idler_m6_nut_loose'])

### z_back ← `assembly_d3d_z_back_bench.json`
Authored in scope: 6 | Coverage of manual: 3/7 (43%)

**Matched (manual → authored):**
- Step 3.1 `Insert M6x18 Bolt` → seq 243 `Insert M6x18 bolt into idler half` (60%)
- Step 3.4 `Secure bearing with one M6x18 bolt` → seq 246 `Bolt idler halves and secure bearing` (60%)
- Step 6.2 `Tighten the idler bolts` → seq 257 `Tighten short idler bolts onto rods` (60%)

**⚠ Manual steps NOT covered (4):**
- p.12 Step 3.2: Insert two bearing pieces
- p.12 Step 3.3: Identify how to put the idler
- p.13 Step 3.5: Put 1 m6x30 screw into correct hole
- p.14 Step 3.6: Secure a M6x18 bolt

**ℹ Authored steps with no manual match (3):**
- seq 245 `Align idler rod holes before closing` (Confirm)
- seq 256 `Insert rods into idler, ends flush` (Place, parts=['z1_spacer_1', 'z1_spacer_002'])
- seq 262 `Route belt around idler bearing` (Place)


---

## IdlerHalves — axes: y_right, z_front (manual pp. 48–50)

**Manual sub-steps (6):**
- p.48 Step 10.1: Insert M6x18 Bolt
- p.48 Step 10.2: Insert two bearing pieces
- p.48 Step 10.3: Identify how to put the idler
- p.49 Step 10.4: Secure bearing with one M6x18
- p.49 Step 10.5: Put 1 m6x30 screw into correct
- p.50 Step 10.6: Secure a M6x18

### y_right ← `assembly_d3d_y_right_bench.json`
Authored in scope: 6 | Coverage of manual: 2/6 (33%)

**Matched (manual → authored):**
- Step 10.1 `Insert M6x18 Bolt` → seq 152 `Insert M6x18 bolt into idler half` (60%)
- Step 10.4 `Secure bearing with one M6x18` → seq 155 `Bolt idler halves and secure bearing` (40%)

**⚠ Manual steps NOT covered (4):**
- p.48 Step 10.2: Insert two bearing pieces
- p.48 Step 10.3: Identify how to put the idler
- p.49 Step 10.5: Put 1 m6x30 screw into correct
- p.50 Step 10.6: Secure a M6x18

**ℹ Authored steps with no manual match (4):**
- seq 154 `Align idler rod holes before closing` (Confirm)
- seq 165 `Insert rods into idler, ends flush` (Place, parts=['rod_007', 'rod_008'])
- seq 166 `Tighten short idler bolts onto rods` (Use)
- seq 171 `Route belt around idler bearing` (Place)

### z_front ← `assembly_d3d_z_front_bench.json`
Authored in scope: 6 | Coverage of manual: 2/6 (33%)

**Matched (manual → authored):**
- Step 10.1 `Insert M6x18 Bolt` → seq 217 `Insert M6x18 bolt into idler half` (60%)
- Step 10.4 `Secure bearing with one M6x18` → seq 220 `Bolt idler halves and secure bearing` (40%)

**⚠ Manual steps NOT covered (4):**
- p.48 Step 10.2: Insert two bearing pieces
- p.48 Step 10.3: Identify how to put the idler
- p.49 Step 10.5: Put 1 m6x30 screw into correct
- p.50 Step 10.6: Secure a M6x18

**ℹ Authored steps with no manual match (4):**
- seq 219 `Align idler rod holes before closing` (Confirm)
- seq 230 `Insert rods into idler, ends flush` (Place, parts=['z2_spacer', 'z2_spacer_2'])
- seq 231 `Tighten short idler bolts onto rods` (Use)
- seq 236 `Route belt around idler bearing` (Place)


---

## MotorHolder — axes: x_axis (manual pp. 74–75)

**Manual sub-steps (4):**
- p.74 Step 16.2: Insert the belt
- p.74 Step 16.3: Close up the motor
- p.75 Step 17.1: Put pulley on motor
- p.75 Step 17.2: Tighten pulley set screw onto motor shaft

### x_axis ← `assembly_d3d_x_axis_bench.json`
Authored in scope: 12 | Coverage of manual: 4/4 (100%)

**Matched (manual → authored):**
- Step 16.2 `Insert the belt` → seq 192 `Insert belt in motor holder channel` (40%)
- Step 16.3 `Close up the motor` → seq 193 `Close motor holder halves together` (40%)
- Step 17.1 `Put pulley on motor` → seq 189 `Mount pulley on motor shaft with spacer` (40%)
- Step 17.2 `Tighten pulley set screw onto motor shaft` → seq 190 `Lock pulley set screw until it pops` (43%)

**ℹ Authored steps with no manual match (8):**
- seq 191 `Load motor holder half with nuts and M6x30 bolt` (Place)
- seq 194 `Confirm belt runs smoothly on pulley` (Confirm)
- seq 195 `Attach motor with 4 M3x25 screws` (Use)
- seq 196 `Insert M6x18 bolts into motor holder` (Use)
- seq 197 `Dangle test: motor holder hangs securely` (Confirm)
- seq 201 `Push motor holder onto rods` (Place)
- seq 214 `Prep motor holder holes for endstop` (Confirm)
- seq 215 `Press endstop holder onto motor holder` (Place)


---

## MotorHolder — axes: y_left, z_back (manual pp. 15–18)

**Manual sub-steps (4):**
- p.15 Step 4.1: Put pulley on motor
- p.15 Step 4.2: Tighten pulley set screw
- p.18 Step 5.2: Set up the other half piece
- p.18 Step 5.3: Close up the two half

### y_left ← `assembly_d3d_y_left_bench.json`
Authored in scope: 12 | Coverage of manual: 2/4 (50%)

**Matched (manual → authored):**
- Step 4.1 `Put pulley on motor` → seq 124 `Mount pulley on motor shaft with spacer` (40%)
- Step 4.2 `Tighten pulley set screw` → seq 125 `Lock pulley set screw until it pops` (43%)

**⚠ Manual steps NOT covered (2):**
- p.18 Step 5.2: Set up the other half piece
- p.18 Step 5.3: Close up the two half

**ℹ Authored steps with no manual match (10):**
- seq 126 `Load motor holder half with nuts and M6x30 bolt` (Place, parts=['y_left_motor_m6_nut_a', 'y_left_motor_m6_nut_b', 'y_left_motor_m6_nut_c'])
- seq 127 `Insert belt in motor holder channel` (Place, parts=['y_left_gt2_belt'])
- seq 128 `Close motor holder halves together` (Place)
- seq 129 `Confirm belt runs smoothly on pulley` (Confirm)
- seq 130 `Attach motor with 4 M3x25 screws` (Place, parts=['y_left_m3x25_a', 'y_left_m3x25_b', 'y_left_m3x25_c', 'y_left_m3x25_d'])
- seq 131 `Insert M6x18 bolts into motor holder` (Place, parts=['y_left_m6x18_c'])
- seq 132 `Dangle test: motor holder hangs securely` (Confirm)
- seq 136 `Push motor holder onto rods` (Place)
- seq 149 `Prep motor holder holes for endstop` (Confirm)
- seq 150 `Press endstop holder onto motor holder` (Place)

### z_back ← `assembly_d3d_z_back_bench.json`
Authored in scope: 10 | Coverage of manual: 2/4 (50%)

**Matched (manual → authored):**
- Step 4.1 `Put pulley on motor` → seq 247 `Mount pulley on motor shaft with spacer` (40%)
- Step 4.2 `Tighten pulley set screw` → seq 248 `Lock pulley set screw until it pops` (43%)

**⚠ Manual steps NOT covered (2):**
- p.18 Step 5.2: Set up the other half piece
- p.18 Step 5.3: Close up the two half

**ℹ Authored steps with no manual match (8):**
- seq 249 `Load motor holder half with nuts and M6x30 bolt` (Place)
- seq 250 `Insert belt in motor holder channel` (Place)
- seq 251 `Close motor holder halves together` (Place)
- seq 252 `Confirm belt runs smoothly on pulley` (Confirm)
- seq 253 `Attach motor with 4 M3x25 screws` (Use)
- seq 254 `Insert M6x18 bolts into motor holder` (Use)
- seq 255 `Dangle test: motor holder hangs securely` (Confirm)
- seq 259 `Push motor holder onto rods` (Place)


---

## MotorHolder — axes: y_right, z_front (manual pp. 51–55)

**Manual sub-steps (6):**
- p.51 Step 11.1: Put pulley on motor
- p.51 Step 11.2: Tighten pulley set screw onto motor shaft
- p.54 Step 10.2: Set up the other
- p.54 Step 10.3: Close up the two
- p.55 Step 10.3: Fasten small bolts into
- p.55 Step 10.4: Fasten 3 m6x18 bolts into motor piece

### y_right ← `assembly_d3d_y_right_bench.json`
Authored in scope: 10 | Coverage of manual: 3/6 (50%)

**Matched (manual → authored):**
- Step 11.1 `Put pulley on motor` → seq 156 `Mount pulley on motor shaft with spacer` (40%)
- Step 11.2 `Tighten pulley set screw onto motor shaft` → seq 157 `Lock pulley set screw until it pops` (43%)
- Step 10.4 `Fasten 3 m6x18 bolts into motor piece` → seq 163 `Insert M6x18 bolts into motor holder` (50%)

**⚠ Manual steps NOT covered (3):**
- p.54 Step 10.2: Set up the other
- p.54 Step 10.3: Close up the two
- p.55 Step 10.3: Fasten small bolts into

**ℹ Authored steps with no manual match (7):**
- seq 158 `Load motor holder half with nuts and M6x30 bolt` (Place)
- seq 159 `Insert belt in motor holder channel` (Place)
- seq 160 `Close motor holder halves together` (Place)
- seq 161 `Confirm belt runs smoothly on pulley` (Confirm)
- seq 162 `Attach motor with 4 M3x25 screws` (Use)
- seq 164 `Dangle test: motor holder hangs securely` (Confirm)
- seq 168 `Push motor holder onto rods` (Place)

### z_front ← `assembly_d3d_z_front_bench.json`
Authored in scope: 10 | Coverage of manual: 3/6 (50%)

**Matched (manual → authored):**
- Step 11.1 `Put pulley on motor` → seq 221 `Mount pulley on motor shaft with spacer` (40%)
- Step 11.2 `Tighten pulley set screw onto motor shaft` → seq 222 `Lock pulley set screw until it pops` (43%)
- Step 10.4 `Fasten 3 m6x18 bolts into motor piece` → seq 228 `Insert M6x18 bolts into motor holder` (50%)

**⚠ Manual steps NOT covered (3):**
- p.54 Step 10.2: Set up the other
- p.54 Step 10.3: Close up the two
- p.55 Step 10.3: Fasten small bolts into

**ℹ Authored steps with no manual match (7):**
- seq 223 `Load motor holder half with nuts and M6x30 bolt` (Place)
- seq 224 `Insert belt in motor holder channel` (Place)
- seq 225 `Close motor holder halves together` (Place)
- seq 226 `Confirm belt runs smoothly on pulley` (Confirm)
- seq 227 `Attach motor with 4 M3x25 screws` (Use)
- seq 229 `Dangle test: motor holder hangs securely` (Confirm)
- seq 233 `Push motor holder onto rods` (Place)


---

## RodAssembly — axes: x_axis (manual pp. 81)

**Manual sub-steps (3):**
- p.81 Step 18.2: Put carriage assembly onto
- p.81 Step 18.3: Put motor piece onto rods
- p.81 Step 18.4: Quality Control Check

### x_axis ← `assembly_d3d_x_axis_bench.json`
Authored in scope: 2 | Coverage of manual: 0/3 (0%)

**⚠ Manual steps NOT covered (3):**
- p.81 Step 18.2: Put carriage assembly onto
- p.81 Step 18.3: Put motor piece onto rods
- p.81 Step 18.4: Quality Control Check

**ℹ Authored steps with no manual match (2):**
- seq 198 `Insert rods into idler, ends flush` (Place, parts=['rod_009', 'rod_010'])
- seq 202 `QC: rods flush, carriage slides freely` (Confirm)


---

## RodAssembly — axes: y_left, z_back (manual pp. 6–26)

**Manual sub-steps (4):**
- p.6 Step 1.4: Test bearings ﬁt
- p.25 Step 6.3: Put carriage assembly onto rods
- p.26 Step 6.4: Put motor piece onto rods
- p.26 Step 6.5: Quality Control Check

### y_left ← `assembly_d3d_y_left_bench.json`
Authored in scope: 2 | Coverage of manual: 1/4 (25%)

**Matched (manual → authored):**
- Step 6.3 `Put carriage assembly onto rods` → seq 137 `QC: rods flush, carriage slides freely` (33%)

**⚠ Manual steps NOT covered (3):**
- p.6 Step 1.4: Test bearings ﬁt
- p.26 Step 6.4: Put motor piece onto rods
- p.26 Step 6.5: Quality Control Check

**ℹ Authored steps with no manual match (1):**
- seq 133 `Insert rods into idler, ends flush` (Place, parts=['rod_005', 'rod_006'])

### z_back ← `assembly_d3d_z_back_bench.json`
Authored in scope: 2 | Coverage of manual: 1/4 (25%)

**Matched (manual → authored):**
- Step 6.3 `Put carriage assembly onto rods` → seq 260 `QC: rods flush, carriage slides freely` (33%)

**⚠ Manual steps NOT covered (3):**
- p.6 Step 1.4: Test bearings ﬁt
- p.26 Step 6.4: Put motor piece onto rods
- p.26 Step 6.5: Quality Control Check

**ℹ Authored steps with no manual match (1):**
- seq 256 `Insert rods into idler, ends flush` (Place, parts=['z1_spacer_1', 'z1_spacer_002'])


---

## RodAssembly — axes: y_right, z_front (manual pp. 42–58)

**Manual sub-steps (6):**
- p.42 Step 8.4: Test bearings ﬁt
- p.56 Step 11.1: Assemble Y-Right Idler piece
- p.57 Step 11.2: Put carriage assembly onto rods
- p.57 Step 11.3: Put motor piece onto rods
- p.58 Step 11.4: Quality Control Rods are Flush
- p.58 Step 11.5: Thread belt into carriage

### y_right ← `assembly_d3d_y_right_bench.json`
Authored in scope: 2 | Coverage of manual: 2/6 (33%)

**Matched (manual → authored):**
- Step 11.2 `Put carriage assembly onto rods` → seq 169 `QC: rods flush, carriage slides freely` (33%)
- Step 11.4 `Quality Control Rods are Flush` → seq 165 `Insert rods into idler, ends flush` (40%)

**⚠ Manual steps NOT covered (4):**
- p.42 Step 8.4: Test bearings ﬁt
- p.56 Step 11.1: Assemble Y-Right Idler piece
- p.57 Step 11.3: Put motor piece onto rods
- p.58 Step 11.5: Thread belt into carriage

### z_front ← `assembly_d3d_z_front_bench.json`
Authored in scope: 2 | Coverage of manual: 2/6 (33%)

**Matched (manual → authored):**
- Step 11.2 `Put carriage assembly onto rods` → seq 234 `QC: rods flush, carriage slides freely` (33%)
- Step 11.4 `Quality Control Rods are Flush` → seq 230 `Insert rods into idler, ends flush` (40%)

**⚠ Manual steps NOT covered (4):**
- p.42 Step 8.4: Test bearings ﬁt
- p.56 Step 11.1: Assemble Y-Right Idler piece
- p.57 Step 11.3: Put motor piece onto rods
- p.58 Step 11.5: Thread belt into carriage
