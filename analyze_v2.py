import csv
from collections import Counter

rows = []
with open(r'D:\GitHub\AIGP_TermProject\AIGP_TermProject_Template\v2_Result.csv', encoding='utf-8-sig') as f:
    reader = csv.DictReader(f)
    for row in reader:
        if row['row_type'] == 'EPISODE':
            rows.append(row)

wins   = [r for r in rows if r['winner'] == 'B']
losses = [r for r in rows if r['winner'] == 'A']
draws  = [r for r in rows if r['winner'] == 'TimeoutDraw']

def avg(lst, key):
    vals = [float(v[key]) for v in lst if v[key] != '']
    return sum(vals) / len(vals) if vals else 0

total = len(rows)
print(f"=== 결과 분포 (총 {total}경기) ===")
print(f"  승 (B wins):    {len(wins):3d}회  ({len(wins)}%)")
print(f"  패 (A wins):    {len(losses):3d}회  ({len(losses)}%)")
print(f"  무 (Timeout): {len(draws):3d}회  ({len(draws)}%)")

print()
print("=== 평균 잔여 HP ===")
print(f"  {'구분':6s}   A(공격형BT)   B(수비형RL)")
print(f"  전체    :  {avg(rows,'agent_a_final_hp'):6.1f} HP     {avg(rows,'agent_b_final_hp'):6.1f} HP")
print(f"  승리시  :  {avg(wins,'agent_a_final_hp'):6.1f} HP     {avg(wins,'agent_b_final_hp'):6.1f} HP")
print(f"  패배시  :  {avg(losses,'agent_a_final_hp'):6.1f} HP     {avg(losses,'agent_b_final_hp'):6.1f} HP")
print(f"  무승부  :  {avg(draws,'agent_a_final_hp'):6.1f} HP     {avg(draws,'agent_b_final_hp'):6.1f} HP")

print()
print("=== 평균 경기 시간 ===")
print(f"  전체:    {avg(rows,'duration_sec'):.2f}s")
print(f"  승리시:  {avg(wins,'duration_sec'):.2f}s")
print(f"  패배시:  {avg(losses,'duration_sec'):.2f}s")
print(f"  무승부:  60.00s (타임아웃)")

print()
print("=== 전투 행동 평균 (회/경기) ===")
print(f"  {'구분':6s}   attack   block   dodge   total")
for group, label in [(rows,'전체'), (wins,'승리'), (losses,'패배'), (draws,'무승부')]:
    a = avg(group, 'attack_count')
    b = avg(group, 'block_count')
    d = avg(group, 'dodge_count')
    t = avg(group, 'total_action_count')
    print(f"  {label:6s}:  {a:5.1f}   {b:5.1f}   {d:5.1f}   {t:5.1f}")

print()
print("=== 전투 행동 비율 (%) ===")
print(f"  {'구분':6s}   attack   block   dodge   toward    back    side")
for group, label in [(rows,'전체'), (wins,'승리'), (losses,'패배'), (draws,'무승부')]:
    a  = avg(group, 'attack_ratio_percent')
    b  = avg(group, 'block_ratio_percent')
    d  = avg(group, 'dodge_ratio_percent')
    tw = avg(group, 'move_toward_ratio_percent')
    bk = avg(group, 'move_back_ratio_percent')
    sd = avg(group, 'move_side_ratio_percent')
    print(f"  {label:6s}:  {a:5.1f}%  {b:5.1f}%  {d:5.1f}%  {tw:6.1f}%  {bk:5.1f}%  {sd:5.1f}%")

print()
print("=== 이동 행동 평균 (회/경기) ===")
print(f"  {'구분':6s}   toward    back    side")
for group, label in [(rows,'전체'), (wins,'승리'), (losses,'패배'), (draws,'무승부')]:
    tw = avg(group, 'move_toward_count')
    bk = avg(group, 'move_back_count')
    sd = avg(group, 'move_side_count')
    print(f"  {label:6s}:  {tw:5.1f}   {bk:5.1f}   {sd:4.1f}")

print()
print("=== 승리 에피소드: B 잔여 HP 분포 ===")
b_hp_wins = Counter(int(float(r['agent_b_final_hp'])) for r in wins)
for hp in sorted(b_hp_wins.keys()):
    bar = '#' * b_hp_wins[hp]
    print(f"  HP={hp:3d}: {b_hp_wins[hp]:2d}회  {bar}")

print()
print("=== 패배 에피소드: A 잔여 HP 분포 ===")
a_hp_loss = Counter(int(float(r['agent_a_final_hp'])) for r in losses)
for hp in sorted(a_hp_loss.keys()):
    bar = '#' * a_hp_loss[hp]
    print(f"  HP={hp:3d}: {a_hp_loss[hp]:2d}회  {bar}")
