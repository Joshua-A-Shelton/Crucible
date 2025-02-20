#include "gtest/gtest.h"
#include <crucible/Crucible.h>
#include <crucible/scripting/ScriptingEngine.h>
using namespace crucible;
using namespace crucible::core;
TEST(Node,Reparent)
{

    Node root(nullptr);

    Node* topLevel1 = root.addChild();
    Node* topLevel2 = root.addChild();
    Node* child = topLevel1->addChild();
    Node* grandchild1 = child->addChild();
    Node* grandchild2 = child->addChild();
    Node* grandchild3 = child->addChild();
    Node* greatGrandchild = grandchild2->addChild();

    GTEST_ASSERT_EQ(root.childrenCount(),2);
    GTEST_ASSERT_EQ(topLevel1->childrenCount(),1);
    GTEST_ASSERT_EQ(topLevel2->childrenCount(),0);
    GTEST_ASSERT_EQ(child->childrenCount(),3);
    GTEST_ASSERT_EQ(grandchild1->childrenCount(),0);
    GTEST_ASSERT_EQ(grandchild2->childrenCount(),1);
    GTEST_ASSERT_EQ(grandchild3->childrenCount(),0);
    GTEST_ASSERT_EQ(greatGrandchild->childrenCount(),0);

    GTEST_ASSERT_EQ(greatGrandchild->getParent(),grandchild2);
    GTEST_ASSERT_EQ(grandchild2->getChild(0),greatGrandchild);

    GTEST_ASSERT_EQ(child->getParent(),topLevel1);
    GTEST_ASSERT_EQ(topLevel1->getChild(0),child);

    child->setParent(topLevel2);

    GTEST_ASSERT_EQ(topLevel1->childrenCount(),0);
    GTEST_ASSERT_EQ(topLevel2->childrenCount(),1);

    GTEST_ASSERT_EQ(child->getParent(),topLevel2);
    GTEST_ASSERT_EQ(topLevel2->getChild(0),child);

    greatGrandchild->setParent(child);
    GTEST_ASSERT_EQ(child->childrenCount(),4);
    GTEST_ASSERT_EQ(grandchild2->childrenCount(),0);
    GTEST_ASSERT_EQ(greatGrandchild->getParent(),child);
    GTEST_ASSERT_EQ(child->getChild(3),greatGrandchild);

}

TEST(Node,NullParent)
{

    Node root(nullptr);
    Node* child = root.addChild();
    Node* sibling = root.addChild();

    child->setParent(nullptr);
    GTEST_ASSERT_EQ(root.childrenCount(),1);
    GTEST_ASSERT_EQ(root.getChild(0),sibling);
    GTEST_ASSERT_EQ(child->getParent(),nullptr);
    //we have to explicitly manage it now that it's got no parent... that's... kinda... sad if you think about it
    delete child;
}

TEST(Node,KillChild)
{
    Node root(nullptr);
    Node* child = root.addChild();
    Node* sibling = root.addChild();
    Node* grandchild = child->addChild();
    root.killChild(0);
    GTEST_ASSERT_EQ(root.childrenCount(),1);
    GTEST_ASSERT_EQ(root.getChild(0),sibling);
}