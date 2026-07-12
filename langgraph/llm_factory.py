"""LLM provider 스위치: 환경 변수로 Anthropic 직결 / OpenRouter 경유를 선택.

그래프 코드는 BaseChatModel 인터페이스에만 의존하므로 provider 교체가
그래프에 영향을 주지 않는다 (.NET DI에서 구현체 교체와 동일한 구조).

.env 설정:
  AGENT_PROVIDER=openrouter
  AGENT_MODEL=deepseek/deepseek-chat-v3.1   # OpenRouter 모델 ID 형식
  OPENROUTER_API_KEY=sk-or-...

  # 또는 (기본값)
  AGENT_PROVIDER=anthropic
  AGENT_MODEL=claude-haiku-4-5
  ANTHROPIC_API_KEY=sk-ant-...

주의: OpenRouter 모델은 반드시 tool calling(tools) 지원 모델이어야 한다.
openrouter.ai/models에서 'Tools' 필터로 확인할 것.
"""

import os

from langchain_core.language_models.chat_models import BaseChatModel


def build_llm() -> BaseChatModel:
    """AGENT_PROVIDER 환경 변수에 따라 LLM 인스턴스를 생성한다."""
    provider = os.getenv("AGENT_PROVIDER", "anthropic").lower()

    if provider == "openrouter":
        from langchain_openrouter import ChatOpenRouter

        return ChatOpenRouter(
            model=os.getenv("AGENT_MODEL", "deepseek/deepseek-chat-v3.1"),
            max_tokens=1024,
            temperature=0,
        )

    from langchain_anthropic import ChatAnthropic

    return ChatAnthropic(
        model=os.getenv("AGENT_MODEL", "claude-haiku-4-5"),
        max_tokens=1024,
    )