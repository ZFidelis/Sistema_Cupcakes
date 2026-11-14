# Loja de Cupcakes Gourmet — Documentação do Projeto Integrador II

**Projeto:** Loja virtual demonstrativa de cupcakes gourmet  
**Disciplina:** Projeto Integrador Transdisciplinar em Engenharia de Software II  
**Origem:** continuidade da atividade de Engenharia de Software I, dedicada ao levantamento de requisitos e à organização de 15 histórias de usuário.

## Apresentação

O projeto consiste em uma loja virtual de cupcakes, acessível pelo navegador de computadores e celulares. Sua finalidade é permitir que um cliente conheça os produtos, organize um carrinho e acompanhe um pedido, enquanto um administrador mantém o catálogo e atualiza o andamento das compras.

A primeira etapa, realizada na PIT I, identificou as necessidades dos usuários e estabeleceu as prioridades do desenvolvimento. Na PIT II, essa proposta foi transformada em uma aplicação web funcional para fins acadêmicos. O sistema utiliza informações e transações demonstrativas: **não realiza cobranças ou entregas reais**.

## Tecnologias e disponibilização

| Elemento | Solução adotada |
| --- | --- |
| Interface | Angular e TypeScript |
| Servidor | C# e ASP.NET Core Web API |
| Banco de dados | PostgreSQL, com Entity Framework Core |
| Autenticação | ASP.NET Core Identity |
| Hospedagem | Render, com banco PostgreSQL no Neon |
| Plataforma | Aplicação web responsiva |
| Modalidade de desenvolvimento | Codificação tradicional |
| Repositório | [Sistema_Cupcakes no GitHub](https://github.com/ZFidelis/Sistema_Cupcakes) |
| Aplicação publicada | [Loja de cupcakes gourmet](https://cupcakes-gourmet-aaxi.onrender.com) |

A publicação foi registrada na documentação técnica em **13 de novembro de 2026**. O serviço gratuito do Render pode demorar a responder após um período sem acessos.

## Funcionalidades apresentadas

O cliente pode se cadastrar, entrar e sair, consultar os cupcakes, aplicar filtros, organizar o carrinho, simular um pagamento e consultar os pedidos realizados. A área administrativa permite gerenciar produtos, atualizar o status de pedidos e consultar relatórios demonstrativos de vendas. Também foram desenvolvidas avaliações dos produtos.

As funcionalidades de pagamento e acompanhamento de entrega foram adaptadas ao contexto acadêmico. Não existe integração com gateway financeiro, transportadora ou localização em tempo real. Confirmações são mostradas no próprio sistema, sem envio de e-mails externos.

## Documentos desta entrega

1. [Requisitos e escopo](requisitos/ESPECIFICACAO.md) — histórias de usuário, regras e limites da versão.
2. [Arquitetura e UML](arquitetura/ARQUITETURA_UML.md) — organização e principais fluxos da aplicação.
3. [Modelo de dados](dados/MODELO_DADOS.md) — entidades, relacionamentos e dicionário simplificado.
4. [Interface e usabilidade](interface/INTERFACE.md) — estrutura das telas, navegação e protótipos.
5. [Manual de uso](MANUAL_USO.md) — utilização pelo cliente e pelo administrador.
6. [Testes e qualidade](testes/TESTES_E_QUALIDADE.md) — resultados registrados, evidências e atividades ainda necessárias.

Os diagramas, wireframes e algumas capturas de tela estão anexados aos documentos correspondentes. A documentação foi resumida para apresentar a **versão desenvolvida**, sem registros intermediários de comandos ou ajustes do ambiente.

## Situação da entrega acadêmica

Há registros de testes técnicos, de publicação da aplicação e de acesso em navegador. **As avaliações de cinco participantes, o laudo final com base nesses feedbacks e os vídeos solicitados pela disciplina ainda dependem da realização e registro pelo estudante.** Os testes automatizados não substituem essas avaliações.

