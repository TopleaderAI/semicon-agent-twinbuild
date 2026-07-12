"""hitl_checkpoints.sqlite에서 가장 최근 스레드의 전체 메시지 트레이스를 덤프한다.

checkpointer가 매 super-step 상태를 저장하므로, 사후에 "모델이 tool_calls를
냈는가 / ToolNode가 뭘 돌려줬는가"를 재실행 없이 확인할 수 있다.
(LangSmith 연동 전까지의 수동 트레이싱 — Week 5에서 대체 예정)

실행:  uv run inspect_last_run.py [db_path]
"""

import sqlite3
import sys

from langchain_core.messages import AIMessage, ToolMessage
from langgraph.checkpoint.sqlite import SqliteSaver

DB = sys.argv[1] if len(sys.argv) > 1 else "hitl_checkpoints.sqlite"

saver = SqliteSaver(sqlite3.connect(DB, check_same_thread=False))

# 전체 checkpoint 중 가장 최근 것의 thread_id를 찾는다
checkpoints = list(saver.list(None))
if not checkpoints:
    sys.exit(f"{DB}에 checkpoint가 없습니다.")

latest = max(checkpoints, key=lambda c: c.checkpoint["ts"])
thread_id = latest.config["configurable"]["thread_id"]
messages = latest.checkpoint["channel_values"].get("messages", [])

print(f"=== thread {thread_id} — 메시지 {len(messages)}개 ===\n")

for i, msg in enumerate(messages):
    kind = type(msg).__name__
    print(f"[{i}] {kind}")

    if isinstance(msg, AIMessage) and msg.tool_calls:
        for tc in msg.tool_calls:
            print(f"    tool_call → {tc['name']}({tc['args']})")
    if isinstance(msg, ToolMessage):
        print(f"    status={getattr(msg, 'status', '?')} tool={msg.name}")

    content = msg.content if isinstance(msg.content, str) else repr(msg.content)
    print(f"    {content[:300]}")
    print()