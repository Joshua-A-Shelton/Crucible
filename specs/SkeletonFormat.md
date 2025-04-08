# Skeleton Chunk Format #
Here in lies the specification for skeletal data for the Crucible Engine

    #1) Size of compressed data     (Little endian 32 bit signed integer)
    #2) Size of uncompressed data   (Little endian 32 bit signed integer)
    #3) Number of total bones       (Little endian 16 bit unsigned integer)
    #4) Bone data stream            (Sequence of LZ4 compressed bytes that represents bones, total length equivelent to #1, uncompressed size equivelent to #2)


The bones in the data stream are layed out thusly (when uncompressed)

    #1) Bone index in the array     (Little endian 16 bit unsigned integer, should be the number of bone being processed -1, eg: the first bone in the stream should have an index of 0, the second an index of 1, etc)
    #2) Bone Transform              (Sequence of 10 little endian 32 bit floating point numbers) *1
    #3) Parent Index                (Little endian 16 bit unsigned integer, index of parent bone, or 0xFFFF for no parent)
    #4) Child Count                 (Little endian 16 bit unsigned integer)
    #5) Children Indexes            (Sequence of #4(Child Count) number of little endian 16 bit unsigned integers) *2

    *1: first 3 floats are positional data- X,Y,Z positions in that order. Next 4 floats are quaternion rotational data- W,X,Y,Z in that order. The last 3 floats are scale data- X,Y,Z in that order
    *2: if child count is 0, there are no child indexes and the next data is the start of the next bone