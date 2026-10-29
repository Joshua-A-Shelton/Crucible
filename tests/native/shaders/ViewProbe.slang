// The camera's GPU test (ADR 0050): copies the frame's ViewConstants, read
// through the root constant buffer as render steps read them, into a buffer
// C++ reads back.
import crucible.shared;

ConstantBuffer<ViewConstants> view : register(b1);

struct CopyConstants
{
    uint output;
};
[[vk::push_constant]] ConstantBuffer<CopyConstants> constants;

[shader("compute")]
[numthreads(1, 1, 1)]
void copyView()
{
    RWStructuredBuffer<ViewConstants> output = ResourceDescriptorHeap[constants.output];
    output[0] = view;
}
