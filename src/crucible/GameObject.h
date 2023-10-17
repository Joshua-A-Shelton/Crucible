#ifndef CRUCIBLE_GAMEOBJECT_H
#define CRUCIBLE_GAMEOBJECT_H
#include <entt/entt.hpp>
#include <unordered_set>
#include <string>
namespace crucible
{

    class GameObject
    {
    public:
        GameObject();
        ~GameObject();
        GameObject(const GameObject&)=delete;
        GameObject& operator=(const GameObject&)=delete;
        GameObject(GameObject&& from);
        GameObject& operator=(GameObject&& from);

        template<class T> void addComponent(T component);
        template<class T> void removeComponent();
        template<class T> T& getComponent();
    private:
        void move(GameObject&& from);
        entt::entity _entity;
        std::string _name;
        bool _moved = false;
    };

} // crucible

#endif //CRUCIBLE_GAMEOBJECT_H
