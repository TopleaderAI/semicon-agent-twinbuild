"""모델이 tool_calls를 내는지 1회 호출로 확인. 실행: uv run tool_probe.py"""
from dotenv import load_dotenv
load_dotenv()

from llm_factory import build_llm
from hitl_agent import TOOLS

llm = build_llm().bind_tools(TOOLS)
resp = llm.invoke("fetch_sk_hynix_news 툴을 호출해서 오늘 뉴스를 가져와줘")

print("model:", getattr(resp, "response_metadata", {}).get("model_name", "?"))
print("tool_calls:", resp.tool_calls or "없음 ← 이 모델은 탈락")
print("content:", str(resp.content)[:200])