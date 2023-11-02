#ifndef CRUCIBLE_SCRIPTINGENGINE_H
#define CRUCIBLE_SCRIPTINGENGINE_H

namespace crucible
{

    class ScriptingEngine
    {
    public:
        static void initialize();
        static void cleanup();
    private:
        static bool loadHostFXR();
    };

} // crucible

#endif //CRUCIBLE_SCRIPTINGENGINE_H
